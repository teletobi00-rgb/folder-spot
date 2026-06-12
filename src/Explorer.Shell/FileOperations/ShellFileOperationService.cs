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

    public Task<FileOperationResult> CopyAsync(IReadOnlyList<string> sourcePaths, string destinationDir)
    {
        if (ValidateSources(sourcePaths) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        var destination = PathUtils.Normalize(destinationDir);
        return RunAsync($"복사 → {destination}", (operation, owned) =>
        {
            var destinationFolder = new ShellFolder(destination);
            owned.Add(destinationFolder);
            foreach (var source in sourcePaths)
            {
                var item = new ShellItem(source);
                owned.Add(item);
                operation.QueueCopyOperation(item, destinationFolder);
            }
        });
    }

    public Task<FileOperationResult> MoveAsync(IReadOnlyList<string> sourcePaths, string destinationDir)
    {
        if (ValidateSources(sourcePaths) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        var destination = PathUtils.Normalize(destinationDir);
        return RunAsync($"이동 → {destination}", (operation, owned) =>
        {
            var destinationFolder = new ShellFolder(destination);
            owned.Add(destinationFolder);
            foreach (var source in sourcePaths)
            {
                var item = new ShellItem(source);
                owned.Add(item);
                operation.QueueMoveOperation(item, destinationFolder);
            }
        });
    }

    public Task<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanent)
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
            permanentDelete: permanent);
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
        bool permanentDelete = false)
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
                    Options = BuildOptions(permanentDelete),
                };
                configure(operation, queuedComObjects);
                operation.PerformOperations();

                return operation.AnyOperationsAborted
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

    private ShellFileOperations.OperationFlags BuildOptions(bool permanentDelete)
    {
        var options = ShellFileOperations.OperationFlags.NoConfirmMkDir;

        if (!permanentDelete)
        {
            // AllowUndo + RecycleOnDelete = 삭제가 휴지통으로 가고 Ctrl+Z 가능
            options |= ShellFileOperations.OperationFlags.AllowUndo
                | ShellFileOperations.OperationFlags.RecycleOnDelete;
        }

        if (_silent)
        {
            options |= ShellFileOperations.OperationFlags.Silent
                | ShellFileOperations.OperationFlags.NoConfirmation
                | ShellFileOperations.OperationFlags.NoErrorUI;
        }

        return options;
    }
}
