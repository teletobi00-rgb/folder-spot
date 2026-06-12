using Explorer.Core.FileSystem;
using Explorer.Indexing.Index;
using Explorer.Indexing.Persistence;
using Explorer.Indexing.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing;

/// <summary>검색이 쓰는 "현재 인덱스" 핸들 — 재구축 완료 시 원자적으로 교체된다.</summary>
public sealed class FileIndexCatalog : IDisposable
{
    private volatile FileIndex _current = new();

    public FileIndex Current => _current;

    /// <summary>새 인덱스로 교체하고 이전 인덱스를 돌려준다 (호출자가 폐기).</summary>
    public FileIndex Swap(FileIndex next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var previous = Interlocked.Exchange(ref _current, next);
        return previous;
    }

    public void Dispose() => _current.Dispose();
}

/// <summary>인덱싱 대상/동작 옵션 (테스트와 진단용 오버라이드 지원).</summary>
public sealed record IndexingOptions
{
    /// <summary>인덱싱할 루트 경로들. null이면 준비된 고정 드라이브 전체.</summary>
    public IReadOnlyList<string>? Roots { get; init; }

    public bool Disabled { get; init; }

    /// <summary>변경 후 스냅샷 저장까지의 지연.</summary>
    public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromMinutes(5);

    public static IndexingOptions FromEnvironment() => new()
    {
        Disabled = Environment.GetEnvironmentVariable("EXPLORER_DISABLE_INDEXING") == "1",
        Roots = Environment.GetEnvironmentVariable("EXPLORER_INDEX_ROOTS") is { Length: > 0 } roots
            ? roots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null,
    };
}

/// <summary>
/// 백그라운드 인덱싱 파이프라인: 스냅샷 즉시 로드(검색 가능) → 전체 재스캔(새 인덱스) →
/// 원자 교체 → FSW 증분 감시 → 주기 스냅샷 저장. 비권한 폴백 경로(Phase 7에서 USN으로 대체).
/// </summary>
public sealed class IndexingService : IHostedService, IDisposable
{
    private readonly FileIndexCatalog _catalog;
    private readonly SqliteIndexSnapshot _snapshot;
    private readonly RecursiveScanSource _scanner;
    private readonly IDriveProvider _drives;
    private readonly IndexingOptions _options;
    private readonly ILogger<IndexingService> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<FswIndexSource> _watchers = [];
    private readonly Lock _rescanGate = new();
    private readonly Lock _saveGate = new();
    private readonly HashSet<string> _pendingRescans = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pipeline;
    private Timer? _snapshotTimer;
    private volatile bool _dirty;

    public IndexingService(
        FileIndexCatalog catalog,
        SqliteIndexSnapshot snapshot,
        RecursiveScanSource scanner,
        IDriveProvider drives,
        IndexingOptions options,
        ILogger<IndexingService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _snapshot = snapshot;
        _scanner = scanner;
        _drives = drives;
        _options = options;
        _logger = logger;
    }

    /// <summary>현재 진행 상태 텍스트 (간단 진단/로그용).</summary>
    public string Status { get; private set; } = "대기";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("인덱싱 비활성화됨 (EXPLORER_DISABLE_INDEXING=1)");
            return Task.CompletedTask;
        }

        _pipeline = Task.Run(() => RunPipelineAsync(_shutdown.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        DisposeWatchers();

        // 종료 저장과 경합하지 않도록 주기 타이머를 먼저 멈춘다.
        _snapshotTimer?.Dispose();
        _snapshotTimer = null;

        if (_pipeline is { } pipeline)
        {
            try
            {
                await pipeline.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
            }
        }

        SaveSnapshotIfDirty();
    }

    public void Dispose()
    {
        _snapshotTimer?.Dispose();
        DisposeWatchers();
        _shutdown.Dispose();
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        try
        {
            // 1) 스냅샷 즉시 복원 — 재스캔이 끝나기 전에도 검색이 동작한다.
            if (_snapshot.TryLoad() is { } restored)
            {
                _catalog.Swap(restored).Dispose();
                _logger.LogInformation("인덱스 스냅샷 복원: {Count:N0}개 항목 — 즉시 검색 가능", restored.Count);
            }

            var roots = ResolveRoots();
            if (roots.Count == 0)
            {
                Status = "인덱싱 대상 없음";
                return;
            }

            // 2) 전체 재스캔은 새 인덱스에 수행 후 원자 교체 (스냅샷이 stale해도 검색 무중단)
            await RescanAllAsync(roots, ct).ConfigureAwait(false);

            // 3) 증분 감시 시작 + 주기 스냅샷
            foreach (var root in roots)
            {
                StartWatcher(root);
            }

            _snapshotTimer = new Timer(
                _ => SaveSnapshotIfDirty(), null, _options.SnapshotInterval, _options.SnapshotInterval);

            Status = $"감시 중 ({_catalog.Current.Count:N0}개 항목)";
            _logger.LogInformation("인덱싱 파이프라인 가동: {Status}", Status);

            // 재스캔 요청(FSW 오버플로) 처리 루프
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                await ProcessPendingRescansAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = "오류";
            _logger.LogError(ex, "인덱싱 파이프라인 중단");
        }
    }

    private IReadOnlyList<string> ResolveRoots()
    {
        if (_options.Roots is { Count: > 0 } configured)
        {
            return configured;
        }

        return [.. _drives.GetDrives()
            .Where(d => d.IsReady && d.Kind == DriveKind.Fixed)
            .Select(d => d.RootPath)];
    }

    private async Task RescanAllAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        var rebuilt = new FileIndex();
        long total = 0;
        foreach (var root in roots)
        {
            Status = $"스캔 중: {root}";
            _logger.LogInformation("인덱스 스캔 시작: {Root}", root);
            try
            {
                total += await _scanner.ScanAsync(root, rebuilt.AddBatch, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                _logger.LogWarning(ex, "루트 스캔 실패 — 건너뜀: {Root}", root);
            }
        }

        _catalog.Swap(rebuilt).Dispose();
        _dirty = true;
        _logger.LogInformation("인덱스 재구축 완료: {Total:N0}개 항목", total);
        SaveSnapshotIfDirty();
    }

    private void StartWatcher(string root)
    {
        try
        {
            var watcher = new FswIndexSource(
                _catalog.Current,
                rescanRoot =>
                {
                    lock (_rescanGate)
                    {
                        _pendingRescans.Add(rescanRoot);
                    }
                },
                LoggerShim.For<FswIndexSource>(_logger),
                onChanged: () => _dirty = true);
            watcher.Start(root);
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "감시 시작 실패: {Root}", root);
        }
    }

    private async Task ProcessPendingRescansAsync(CancellationToken ct)
    {
        string[] pending;
        lock (_rescanGate)
        {
            if (_pendingRescans.Count == 0)
            {
                return;
            }

            pending = [.. _pendingRescans];
            _pendingRescans.Clear();
        }

        _logger.LogInformation("이벤트 유실 복구 — 재스캔: {Roots}", string.Join(", ", pending));
        var roots = ResolveRoots();
        DisposeWatchers();
        await RescanAllAsync(roots, ct).ConfigureAwait(false);
        foreach (var root in roots)
        {
            StartWatcher(root);
        }
    }

    private void SaveSnapshotIfDirty()
    {
        // 타이머/파이프라인/종료 경로가 겹칠 수 있다 — 스냅샷 쓰기는 한 번에 하나만.
        lock (_saveGate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            if (!_snapshot.TrySave(_catalog.Current))
            {
                _dirty = true;
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    /// <summary>카테고리만 다른 로거 어댑터 (DI 밖에서 워처를 만들 때 사용).</summary>
    private static class LoggerShim
    {
        public static ILogger<T> For<T>(ILogger inner) => new Adapter<T>(inner);

        private sealed class Adapter<T>(ILogger inner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
