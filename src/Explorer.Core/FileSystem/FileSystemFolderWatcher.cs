using Microsoft.Extensions.Logging;

namespace Explorer.Core.FileSystem;

public sealed class FileSystemFolderWatcher : IFolderWatcher
{
    private readonly ILogger<FileSystemFolderWatcher> _logger;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public FileSystemFolderWatcher(ILogger<FileSystemFolderWatcher> logger, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
    }

    public event EventHandler? ChangesDetected;

    public void Watch(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisposeWatcher();

            try
            {
                _watcher = new FileSystemWatcher(directoryPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                   | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                    InternalBufferSize = 64 * 1024,
                };
                _watcher.Created += OnFileSystemEvent;
                _watcher.Deleted += OnFileSystemEvent;
                _watcher.Renamed += OnFileSystemEvent;
                _watcher.Changed += OnFileSystemEvent;
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // 네트워크/제거된 드라이브 등 감시 불가 폴더 — 자동 새로고침 없이 동작한다.
                _logger.LogDebug(ex, "폴더 감시를 시작할 수 없습니다: {Path}", directoryPath);
                DisposeWatcher();
            }
        }
    }

    public void StopWatching()
    {
        lock (_gate)
        {
            DisposeWatcher();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWatcher();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => ScheduleNotification();

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // 버퍼 오버플로 등 — 이벤트가 유실됐을 수 있으니 새로고침을 한 번 트리거한다.
        _logger.LogDebug(e.GetException(), "폴더 감시 오류 — 새로고침 트리거");
        ScheduleNotification();
    }

    private void ScheduleNotification()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Timer.Dispose 후에도 이미 큐에 들어간 콜백이 한 번 더 실행될 수 있어 _disposed를 재확인한다.
            _timer ??= new Timer(_ =>
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                }

                ChangesDetected?.Invoke(this, EventArgs.Empty);
            });
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void DisposeWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
