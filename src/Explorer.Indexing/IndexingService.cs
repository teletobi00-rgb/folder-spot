using System.Runtime;
using Explorer.Core.FileSystem;
using Explorer.Indexing.Index;
using Explorer.Indexing.Persistence;
using Explorer.Indexing.Sources;
using Explorer.Indexing.Usn;
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

    /// <summary>NTFS MFT/USN 고속 경로 사용(옵트인). 끄면 재귀+FSW 폴백.</summary>
    public bool FastIndexingEnabled { get; init; }

    /// <summary>네트워크/매핑 드라이브도 인덱싱(옵트인 — 재귀 스캔, 느림).</summary>
    public bool IndexNetworkDrives { get; init; }

    /// <summary>권한 헬퍼 실행 파일 경로 (없으면 USN 비활성).</summary>
    public string? HelperPath { get; init; }

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
    private readonly List<UsnIndexSource> _usnSources = [];
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
        DisposeSources();

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
        DisposeSources();
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

            // 2) 전체 재구축은 새 인덱스에 수행 후 원자 교체 (스냅샷이 stale해도 검색 무중단).
            //    USN 고속 경로 볼륨은 자체 tailing을 시작하고, 나머지는 FSW 감시가 필요하다.
            var fallbackRoots = await RescanAllAsync(roots, ct).ConfigureAwait(false);

            // 3) FSW 증분 감시는 폴백 볼륨에만 (USN 볼륨은 이미 tailing 중) + 주기 스냅샷
            foreach (var root in fallbackRoots)
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
            .Where(d => d.IsReady
                && (d.Kind == DriveKind.Fixed
                    || (_options.IndexNetworkDrives && d.Kind == DriveKind.Network)))
            .Select(d => d.RootPath)];
    }

    /// <summary>전체 재구축. USN 고속 경로/재귀 폴백을 볼륨별로 선택하고, FSW가 필요한 폴백 볼륨 목록을 돌려준다.</summary>
    private async Task<IReadOnlyList<string>> RescanAllAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        var rebuilt = new FileIndex();
        var fallbackRoots = new List<string>();
        var helperAvailable = _options.HelperPath is { } hp && UsnIndexSource.HelperExists(hp);

        foreach (var root in roots)
        {
            var mode = IndexSourceSelector.Select(
                DriveKindOf(root),
                IndexSourceSelector.IsNtfsVolume(root),
                _options.FastIndexingEnabled,
                helperAvailable);

            // onChange는 재구축 인덱스를 직접 겨냥한다 — 스왑 후 이 인덱스가 곧 current가 되므로
            // tailing 델타가 일관되게 반영된다.
            // 동시성: 앞 볼륨의 tailing(ApplyChange→AddOrUpdate)과 뒤 볼륨의 열거(AddBatch)가
            // 같은 rebuilt를 동시에 쓸 수 있다. FileIndex의 모든 변경 경로가 쓰기 락을 잡으므로
            // 직렬화되어 데이터 손상은 없다(검색의 읽기 락과도 안전).
            if (mode == IndexSourceMode.UsnFast
                && await TryUsnRescanAsync(root, rebuilt, ct).ConfigureAwait(false))
            {
                continue;
            }

            await RecursiveRescanAsync(root, rebuilt, ct).ConfigureAwait(false);
            fallbackRoots.Add(root);
        }

        _catalog.Swap(rebuilt).Dispose();
        _dirty = true;
        _logger.LogInformation("인덱스 재구축 완료: {Total:N0}개 항목 (USN {Usn}개 볼륨)",
            rebuilt.Count, _usnSources.Count);
        SaveSnapshotIfDirty();

        // 재구축으로 버려진 이전 인덱스(대형 LOH 배열)와 스캔 중 생긴 가비지를 OS에 반환한다.
        TrimMemory();
        return fallbackRoots;
    }

    /// <summary>대규모 재구축 직후 LOH 압축 + 워킹셋 트림으로 메모리를 OS에 반환한다(백그라운드 스레드에서만 호출).</summary>
    private void TrimMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            EmptyWorkingSet(GetCurrentProcess());
            _logger.LogDebug("인덱싱 후 메모리 트림 완료");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 트림은 최적화일 뿐 — 실패해도 기능엔 영향 없음.
            _logger.LogDebug(ex, "워킹셋 트림 생략");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>USN 고속 경로 시도. 열거 성공 시 true(소스는 tailing 유지), 실패 시 false(호출자 폴백).</summary>
    private async Task<bool> TryUsnRescanAsync(string root, FileIndex rebuilt, CancellationToken ct)
    {
        Status = $"고속 인덱싱(USN): {root}";
        _logger.LogInformation("USN 고속 인덱싱 시작: {Root}", root);
        var source = new UsnIndexSource(_options.HelperPath!, LoggerShim.For<UsnIndexSource>(_logger));
        try
        {
            var result = await source.StartAsync(
                root,
                items => rebuilt.AddBatch(IndexPathUpdater.FilterExcluded(items)),
                change => ApplyChange(rebuilt, change),
                ct).ConfigureAwait(false);

            if (result == UsnStartResult.Enumerated)
            {
                _usnSources.Add(source);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "USN 고속 인덱싱 실패 — 폴백: {Root}", root);
        }

        source.Dispose();
        return false;
    }

    private async Task RecursiveRescanAsync(string root, FileIndex rebuilt, CancellationToken ct)
    {
        Status = $"스캔 중: {root}";
        _logger.LogInformation("인덱스 스캔 시작(폴백): {Root}", root);
        try
        {
            await _scanner.ScanAsync(root, rebuilt.AddBatch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "루트 스캔 실패 — 건너뜀: {Root}", root);
        }
    }

    private DriveKind DriveKindOf(string root) =>
        _drives.GetDrives().FirstOrDefault(d =>
            string.Equals(d.RootPath, root, StringComparison.OrdinalIgnoreCase)) is { } drive
            ? drive.Kind
            : DriveKind.Fixed; // 진단용 명시 루트는 고정으로 간주

    /// <summary>USN/FSW 변경 델타를 인덱스에 반영한다.</summary>
    private static void ApplyChange(FileIndex index, in UsnChange change)
    {
        switch (change.Kind)
        {
            case FileChangeKind.Created:
                // 생성은 트리 전체가 새로 나타날 수 있어 하위까지 반영한다.
                IndexPathUpdater.AddExistingPathTree(index, change.FullPath, change.IsDirectory);
                break;
            case FileChangeKind.Modified:
                // 변경은 해당 항목만 — 디렉터리 하위 재열거 폭주를 피한다.
                IndexPathUpdater.AddSinglePath(index, change.FullPath, change.IsDirectory);
                break;
            case FileChangeKind.Deleted:
                index.RemoveSubtree(change.FullPath);
                break;
            case FileChangeKind.Renamed when change.OldFullPath is { } oldPath:
                ApplyRename(index, oldPath, change.FullPath, change.IsDirectory);
                break;
        }
    }

    private static void ApplyRename(FileIndex index, string oldPath, string newPath, bool isDirectory)
    {
        var oldParent = Path.GetDirectoryName(oldPath);
        var newParent = Path.GetDirectoryName(newPath);
        var newName = Path.GetFileName(newPath);

        // 같은 부모 내 이름변경이면 O(1) Rename으로 하위 경로까지 보존된다.
        if (string.Equals(oldParent, newParent, StringComparison.OrdinalIgnoreCase) && newName.Length > 0)
        {
            index.Rename(oldPath, newName);
            return;
        }

        // 다른 폴더로의 이동: 옛 위치를 제거하고 새 위치의 현재 트리를 다시 반영한다.
        index.RemoveSubtree(oldPath);
        IndexPathUpdater.AddExistingPathTree(index, newPath, isDirectory);
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
        DisposeSources();
        var fallbackRoots = await RescanAllAsync(roots, ct).ConfigureAwait(false);
        foreach (var root in fallbackRoots)
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

    private void DisposeSources()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

        foreach (var source in _usnSources)
        {
            source.Dispose(); // 파이프 닫힘 → 헬퍼 종료
        }

        _usnSources.Clear();
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
