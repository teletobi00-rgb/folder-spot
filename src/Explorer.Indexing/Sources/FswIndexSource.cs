using Explorer.Indexing.Index;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing.Sources;

/// <summary>
/// FileSystemWatcher 기반 증분 갱신 소스. 변경 이벤트를 인덱스 델타로 직접 반영하고,
/// 내부 버퍼 오버플로(이벤트 유실) 시 해당 루트 재스캔을 요청한다 (R-FSWLOSS).
/// </summary>
public sealed class FswIndexSource : IDisposable
{
    private readonly IFileIndex _index;
    private readonly ILogger<FswIndexSource> _logger;
    private readonly Action<string> _requestRescan;
    private readonly Action? _onChanged;
    private FileSystemWatcher? _watcher;

    public FswIndexSource(
        IFileIndex index,
        Action<string> requestRescan,
        ILogger<FswIndexSource> logger,
        Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(requestRescan);
        ArgumentNullException.ThrowIfNull(logger);
        _index = index;
        _requestRescan = requestRescan;
        _logger = logger;
        _onChanged = onChanged;
    }

    public string? RootPath { get; private set; }

    public void Start(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (_watcher is not null)
        {
            throw new InvalidOperationException("이미 감시 중입니다.");
        }

        RootPath = rootPath;
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
        };
        // 생성은 트리 전체가 새로 나타날 수 있으므로(폴더 이동·이입) 하위까지 반영하고,
        // 변경은 해당 항목만 갱신한다 — Changed는 자식 추가/삭제마다 부모 디렉터리에도 쏟아지므로
        // 매번 하위를 재열거하면 대량 파일 작업에서 O(N^2)로 폭발한다.
        _watcher.Created += (_, e) => Handle(() => OnCreated(e.FullPath));
        _watcher.Changed += (_, e) => Handle(() => OnChanged(e.FullPath));
        _watcher.Deleted += (_, e) => Handle(() => _index.RemoveSubtree(e.FullPath));
        _watcher.Renamed += (_, e) => Handle(() => OnRenamed(e.OldFullPath, e.FullPath));
        _watcher.Error += (_, e) => OnError(e.GetException());
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>테스트/오버플로 경로용 — 이벤트 유실 시 재스캔 요청.</summary>
    internal void OnError(Exception exception)
    {
        _logger.LogWarning(exception, "파일 감시 오류 — 루트 재스캔 요청: {Root}", RootPath);
        if (RootPath is { } root)
        {
            _requestRescan(root);
        }
    }

    private void Handle(Action action)
    {
        try
        {
            action();
            _onChanged?.Invoke();
        }
        catch (ObjectDisposedException ex)
        {
            // 재스캔 교체 직후 옛 워처의 잔여 이벤트가 폐기된 인덱스에 닿은 경우 — 진단을 위해 남긴다.
            _logger.LogInformation(ex, "폐기된 인덱스로의 감시 이벤트 — 무시 (재스캔 교체 직후)");
        }
        catch (Exception ex)
        {
            // 감시 콜백의 어떤 예외도 워처 스레드를 죽이면 안 된다.
            _logger.LogDebug(ex, "감시 이벤트 처리 실패 — 무시");
        }
    }

    private void OnCreated(string fullPath)
    {
        // 이벤트와 실제 상태 사이의 레이스 — 디스크의 현재 상태를 기준으로 반영한다.
        // 폴더가 내용과 함께 한 번에 나타나는 경우(이입/이동)를 위해 하위까지 재열거한다.
        RetryIfPathNotReady(() => IndexPathUpdater.AddExistingPathTree(_index, fullPath, isDirectoryHint: false));
    }

    private void OnChanged(string fullPath)
    {
        // 변경은 해당 항목만 갱신 — 디렉터리 하위는 다시 걷지 않는다.
        IndexPathUpdater.AddSinglePath(_index, fullPath, isDirectoryHint: false);
    }

    private void OnRenamed(string oldFullPath, string newFullPath)
    {
        var oldParent = Path.GetDirectoryName(oldFullPath);
        var newParent = Path.GetDirectoryName(newFullPath);
        var newName = Path.GetFileName(newFullPath);
        if (string.IsNullOrEmpty(newName))
        {
            return;
        }

        if (string.Equals(oldParent, newParent, StringComparison.OrdinalIgnoreCase))
        {
            _index.Rename(oldFullPath, newName);
            return;
        }

        // 다른 폴더로의 이동으로 보고되는 경우 — 제거 후 재추가
        _index.RemoveSubtree(oldFullPath);
        RetryIfPathNotReady(() =>
            IndexPathUpdater.AddExistingPathTree(_index, newFullPath, isDirectoryHint: Directory.Exists(newFullPath)));
    }

    private static void RetryIfPathNotReady(Func<bool> update)
    {
        if (update())
        {
            return;
        }

        Thread.Sleep(50);
        update();
    }
}
