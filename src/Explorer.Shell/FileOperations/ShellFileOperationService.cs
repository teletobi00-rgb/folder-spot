using System.IO;
using System.Runtime.InteropServices;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Shell.Threading;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Explorer.Shell.FileOperations;

/// <summary>
/// Windows IFileOperation 기반 구현. 휴지통/undo/충돌·진행 다이얼로그 등 셸 표준 동작을 그대로 얻는다.
/// COM 요구사항 때문에 전용 STA 스레드에서 순차 실행된다.
/// </summary>
public sealed class ShellFileOperationService : IFileOperationService, IDisposable
{
    private readonly StaWorker _worker = new("Explorer.FileOps");
    private readonly Func<nint> _ownerWindowResolver;
    private readonly bool _silent;
    private readonly ILogger<ShellFileOperationService> _logger;

    /// <param name="ownerWindowResolver">다이얼로그 소유자 HWND (UI 스레드에서 호출됨).</param>
    /// <param name="silent">true면 모든 셸 UI 억제 — 테스트용.</param>
    public ShellFileOperationService(
        Func<nint> ownerWindowResolver,
        ILogger<ShellFileOperationService> logger,
        bool silent = false)
    {
        ArgumentNullException.ThrowIfNull(ownerWindowResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _ownerWindowResolver = ownerWindowResolver;
        _logger = logger;
        _silent = silent;
    }

    public Task<FileOperationResult> CopyAsync(
        IReadOnlyList<string> sourcePaths, string destinationDir, FileOperationContext? context = null)
    {
        if (ValidateSources(sourcePaths) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        var destination = PathUtils.Normalize(destinationDir);
        return RunAsync(
            $"복사 → {destination}",
            (operation, owned) =>
            {
                var destinationFolder = new ShellFolder(destination);
                owned.Add(destinationFolder);
                foreach (var source in sourcePaths)
                {
                    var item = new ShellItem(source);
                    owned.Add(item);
                    operation.QueueCopyOperation(item, destinationFolder);
                }
            },
            context: context,
            completedKind: OperationKind.Copy);
    }

    public Task<FileOperationResult> MoveAsync(
        IReadOnlyList<string> sourcePaths, string destinationDir, FileOperationContext? context = null)
    {
        if (ValidateSources(sourcePaths) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        var destination = PathUtils.Normalize(destinationDir);
        return RunAsync(
            $"이동 → {destination}",
            (operation, owned) =>
            {
                var destinationFolder = new ShellFolder(destination);
                owned.Add(destinationFolder);
                foreach (var source in sourcePaths)
                {
                    var item = new ShellItem(source);
                    owned.Add(item);
                    operation.QueueMoveOperation(item, destinationFolder);
                }
            },
            context: context,
            completedKind: OperationKind.Move);
    }

    public Task<FileOperationResult> DeleteAsync(
        IReadOnlyList<string> paths, bool permanent, FileOperationContext? context = null)
    {
        if (ValidateSources(paths) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        return RunAsync(
            permanent ? "영구 삭제" : "휴지통으로 삭제",
            (operation, owned) =>
            {
                foreach (var path in paths)
                {
                    var item = new ShellItem(path);
                    owned.Add(item);
                    operation.QueueDeleteOperation(item);
                }
            },
            permanentDelete: permanent,
            context: context,
            completedKind: permanent ? OperationKind.DeletePermanent : OperationKind.Delete);
    }

    public Task<FileOperationResult> MoveItemAsync(string sourcePath, string destinationDir, string? newName = null)
    {
        var source = PathUtils.Normalize(sourcePath);
        var destination = PathUtils.Normalize(destinationDir);
        return RunAsync($"이동 → {destination}", (operation, owned) =>
        {
            var destinationFolder = new ShellFolder(destination);
            owned.Add(destinationFolder);
            var item = new ShellItem(source);
            owned.Add(item);
            operation.QueueMoveOperation(item, destinationFolder, newName);
        });
    }

    public Task<FileOperationResult> RenameAsync(string path, string newName)
    {
        if (!FileNameRules.IsValid(newName, out var reason))
        {
            return Task.FromResult(FileOperationResult.Failure(FileOperationError.InvalidName, reason));
        }

        var normalized = PathUtils.Normalize(path);
        return RunAsync($"이름 변경 → {newName}", (operation, owned) =>
        {
            var item = new ShellItem(normalized);
            owned.Add(item);
            operation.QueueRenameOperation(item, newName);
        });
    }

    public Task<FileOperationResult> CreateFolderAsync(string parentDir, string name)
    {
        if (!FileNameRules.IsValid(name, out var reason))
        {
            return Task.FromResult(FileOperationResult.Failure(FileOperationError.InvalidName, reason));
        }

        var parent = PathUtils.Normalize(parentDir);
        return RunAsync($"새 폴더: {name}", (operation, owned) =>
        {
            var parentFolder = new ShellFolder(parent);
            owned.Add(parentFolder);
            operation.QueueNewItemOperation(parentFolder, name, FileAttributes.Directory);
        });
    }

    public void Dispose() => _worker.Dispose();

    private static FileOperationResult? ValidateSources(IReadOnlyList<string> paths)
    {
        if (paths is not { Count: > 0 })
        {
            return FileOperationResult.Failure(FileOperationError.InvalidName, "대상 항목이 없습니다.");
        }

        return null;
    }

    private async Task<FileOperationResult> RunAsync(
        string description,
        Action<ShellFileOperations, List<IDisposable>> configure,
        bool permanentDelete = false,
        FileOperationContext? context = null,
        OperationKind completedKind = OperationKind.Copy)
    {
        var owner = _silent ? 0 : _ownerWindowResolver();

        var result = await _worker.RunAsync(() =>
        {
            // 큐에 넣은 ShellItem/ShellFolder COM 래퍼는 PerformOperations가 끝날 때까지 살아 있어야 한다
            // (먼저 해제하면 해제된 COM 포인터를 셸이 역참조 — 네트워크 경로/셸 확장에서 크래시).
            var queuedComObjects = new List<IDisposable>();
            try
            {
                using var operation = new ShellFileOperations
                {
                    OwnerWindow = new HWND(owner),
                    Options = BuildOptions(permanentDelete, context),
                };
                if (context is not null)
                {
                    AttachContext(operation, context, completedKind);
                }

                configure(operation, queuedComObjects);
                operation.PerformOperations();

                // 게이트가 항목을 취소 HRESULT로 거부하면 엔진은 '건너뜀'으로 처리하고
                // AnyOperationsAborted를 세우지 않는 경우가 있다 — 우리 컨트롤 상태를 함께 본다.
                var cancelled = operation.AnyOperationsAborted
                    || context?.Control?.IsCancellationRequested == true;
                return cancelled
                    ? FileOperationResult.Cancelled()
                    : FileOperationResult.Success();
            }
            catch (COMException ex)
            {
                var error = FileOperationErrorMapper.FromHResult(ex.HResult);
                return error == FileOperationError.Cancelled
                    ? FileOperationResult.Cancelled()
                    : FileOperationResult.Failure(error, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or FileNotFoundException or DirectoryNotFoundException)
            {
                return FileOperationResult.Failure(FileOperationErrorMapper.FromException(ex), ex.Message);
            }
            finally
            {
                foreach (var comObject in queuedComObjects)
                {
                    comObject.Dispose();
                }
            }
        }).ConfigureAwait(false);

        if (!result.Succeeded && !result.Aborted)
        {
            _logger.LogWarning("파일 작업 실패 ({Description}): {Error} {Message}", description, result.Error, result.Message);
        }
        else
        {
            _logger.LogDebug("파일 작업 완료 ({Description}): 성공={Succeeded} 중단={Aborted}", description, result.Succeeded, result.Aborted);
        }

        return result;
    }

    private ShellFileOperations.OperationFlags BuildOptions(bool permanentDelete, FileOperationContext? context)
    {
        var options = ShellFileOperations.OperationFlags.NoConfirmMkDir;

        if (!permanentDelete)
        {
            // AllowUndo + RecycleOnDelete = 삭제가 휴지통으로 가고 Ctrl+Z 가능
            options |= ShellFileOperations.OperationFlags.AllowUndo
                | ShellFileOperations.OperationFlags.RecycleOnDelete;
        }

        if (context is not null)
        {
            // 진행/오류/충돌 UI는 큐가 담당 — 셸 UI는 모두 억제한다.
            options |= ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoErrorUI;
            options |= context.Collision switch
            {
                CollisionOption.Overwrite => ShellFileOperations.OperationFlags.NoConfirmation,
                CollisionOption.KeepBoth => ShellFileOperations.OperationFlags.RenameOnCollision,
                _ => 0,
            };
        }

        if (_silent)
        {
            options |= ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoConfirmation
                | ShellFileOperations.OperationFlags.NoErrorUI;
        }

        return options;
    }

    /// <summary>일시정지 게이트/취소/진행·완료 콜백을 셸 작업 이벤트에 연결한다 (핸들러는 작업 STA 스레드에서 실행).</summary>
    private static void AttachContext(ShellFileOperations operation, FileOperationContext context, OperationKind kind)
    {
        var control = context.Control;
        var events = context.Events;

        if (control is not null)
        {
            operation.PreCopyItem += (_, _) => ThrottleGate(control);
            operation.PreMoveItem += (_, _) => ThrottleGate(control);
            operation.PreDeleteItem += (_, _) => ThrottleGate(control);
        }

        operation.UpdateProgress += (_, e) =>
        {
            if (control is not null)
            {
                ThrottleGate(control);
            }

            events?.OnProgress(e.ProgressPercentage);
        };

        if (events is not null)
        {
            operation.PostCopyItem += (_, e) => ReportCompleted(events, OperationKind.Copy, e);
            operation.PostMoveItem += (_, e) => ReportCompleted(events, OperationKind.Move, e);
            operation.PostDeleteItem += (_, e) => ReportCompleted(events, kind, e);
        }
    }

    /// <summary>일시정지면 재개까지 블로킹, 취소면 셸 엔진에 사용자 취소를 전달해 중단시킨다.</summary>
    private static void ThrottleGate(OperationControl control)
    {
        control.WaitIfPaused();
        if (control.IsCancellationRequested)
        {
            // Vanara sink가 이 예외를 잡아 ErrorCode를 엔진 HRESULT로 반환한다 → AnyOperationsAborted.
            // COMException이 유일하게 ErrorCode를 그대로 전달하는 통로라 의도적으로 사용한다.
#pragma warning disable CA2201
            throw new COMException("사용자 취소", unchecked((int)0x80270000)); // COPYENGINE_E_USER_CANCELLED
#pragma warning restore CA2201
        }
    }

    private static void ReportCompleted(IOperationEvents events, OperationKind kind, ShellFileOperations.ShellFileOpEventArgs e)
    {
        if (e.Result.Failed)
        {
            return;
        }

        var sourcePath = TryGetPath(e.SourceItem);
        if (sourcePath is null)
        {
            return;
        }

        var newPath = TryGetPath(e.DestItem);
        if (newPath is null && TryGetPath(e.DestFolder) is { } folder && e.Name is { Length: > 0 } name)
        {
            newPath = Path.Combine(folder, name);
        }

        events.OnItemCompleted(new CompletedItem
        {
            Kind = kind,
            SourcePath = sourcePath,
            NewPath = newPath,
        });
    }

    private static string? TryGetPath(ShellItem? item)
    {
        try
        {
            return item?.FileSystemPath;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
