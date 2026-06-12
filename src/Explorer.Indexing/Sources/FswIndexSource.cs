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
        _watcher.Created += (_, e) => Handle(() => OnCreatedOrChanged(e.FullPath));
        _watcher.Changed += (_, e) => Handle(() => OnCreatedOrChanged(e.FullPath));
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

    private void OnCreatedOrChanged(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            return;
        }

        // 이벤트와 실제 상태 사이의 레이스 — 디스크의 현재 상태를 기준으로 반영한다.
        if (Directory.Exists(fullPath))
        {
            _index.AddOrUpdate(new IndexItem(parent, name, IsDirectory: true, 0, 0));
            return;
        }

        if (File.Exists(fullPath))
        {
            long size = 0;
            long modified = 0;
            try
            {
                var info = new FileInfo(fullPath);
                size = info.Length;
                modified = info.LastWriteTime.Ticks;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            _index.AddOrUpdate(new IndexItem(parent, name, IsDirectory: false, size, modified));
        }
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
        OnCreatedOrChanged(newFullPath);
    }
}
