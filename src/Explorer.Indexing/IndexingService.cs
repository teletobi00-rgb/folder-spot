using System.Runtime;
using Explorer.Core;
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
    private readonly Lock _gate = new();
    private Entry _current = new(new FileIndex());
    private bool _disposed;
    private volatile int _lastKnownCount;

    /// <summary>
    /// 현재 인덱스 원본 참조. 즉시 끝나는 테스트/초기화용이며, 비동기 작업은 <see cref="Acquire"/>를 사용한다.
    /// </summary>
    public FileIndex Current
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _current.Index;
            }
        }
    }

    public int Count
    {
        get
        {
            using var lease = Acquire();
            return lease.Index.Count;
        }
    }

    /// <summary>마지막 Swap/갱신 시점의 항목 수 — UI 스레드가 락 없이 즉시 읽기 위한 캐시(증분 변경엔 약간 지연 가능).</summary>
    public int LastKnownCount => _lastKnownCount;

    /// <summary>현재 인덱스 항목 수로 캐시를 갱신한다(백그라운드에서만 호출 — 읽기 락을 잡는다).</summary>
    public void RefreshLastKnownCount()
    {
        using var lease = Acquire();
        _lastKnownCount = lease.Index.Count;
    }

    public Lease Acquire()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _current.RefCount++;
            return new Lease(this, _current);
        }
    }

    /// <summary>새 인덱스로 교체한다. 이전 인덱스는 진행 중인 lease가 모두 끝난 뒤 폐기된다.</summary>
    public void Swap(FileIndex next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Entry previous;
        var disposePrevious = false;
        lock (_gate)
        {
            ThrowIfDisposed();
            previous = _current;
            _current = new Entry(next);
            previous.Retired = true;
            disposePrevious = previous.RefCount == 0;
        }

        _lastKnownCount = next.Count; // 락 밖에서 캐시 갱신(next는 아직 비공유라 비경합)

        if (disposePrevious)
        {
            previous.Index.Dispose();
        }
    }

    public void Dispose()
    {
        Entry current;
        var disposeCurrent = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            current.Retired = true;
            disposeCurrent = current.RefCount == 0;
        }

        if (disposeCurrent)
        {
            current.Index.Dispose();
        }
    }

    private void Release(Entry entry)
    {
        var dispose = false;
        lock (_gate)
        {
            if (entry.RefCount == 0)
            {
                return;
            }

            entry.RefCount--;
            dispose = entry.Retired && entry.RefCount == 0;
        }

        if (dispose)
        {
            entry.Index.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal sealed class Entry(FileIndex index)
    {
        public FileIndex Index { get; } = index;

        public int RefCount { get; set; }

        public bool Retired { get; set; }
    }

    public sealed class Lease : IDisposable
    {
        private FileIndexCatalog? _owner;
        private readonly Entry _entry;

        internal Lease(FileIndexCatalog owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public FileIndex Index => _entry.Index;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_entry);
        }
    }
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

    /// <summary>
    /// 스냅샷이 이보다 최근이면 시작 시 전체 재스캔을 건너뛰고 FSW 증분만 시작한다(시작 CPU/IO 절감).
    /// null이면 항상 재스캔. 닫혀 있던 동안의 변경은 다음 전체 재스캔(FSW 오버플로 등)까지 반영이 늦을 수 있다.
    /// USN 고속 경로가 켜져 있으면(전체 MFT 열거 필요) 이 값과 무관하게 항상 재스캔한다.
    /// </summary>
    public TimeSpan? StartupRescanSkipMaxAge { get; init; } = TimeSpan.FromHours(6);

    /// <summary>네트워크 드라이브 한 곳당 인덱싱 항목 상한 — 대용량 NAS가 메모리를 폭주시키지 않게 한다.</summary>
    public long NetworkScanMaxItems { get; init; } = 100_000;

    /// <summary>네트워크 드라이브 한 곳당 스캔 시간 예산 — 느린 NAS가 시작을 막지 않게 한다(초과 시 일부만).</summary>
    public TimeSpan NetworkScanTimeBudget { get; init; } = TimeSpan.FromSeconds(60);

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
    private readonly List<ActiveWatcher> _watchers = [];
    private readonly List<UsnIndexSource> _usnSources = [];
    private readonly Lock _rescanGate = new();
    private readonly Lock _saveGate = new();
    private readonly HashSet<string> _pendingRescans = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DriveIndexProgress> _driveState =
        new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>드라이브별 인덱싱 진행 상태 스냅샷.</summary>
    public IReadOnlyCollection<DriveIndexProgress> DriveProgress => _driveState.Values.ToArray();

    /// <summary>드라이브 진행 상태가 바뀔 때 발생(인덱싱 백그라운드 스레드) — 구독자는 UI 스레드로 마샬링할 것.</summary>
    public event EventHandler<DriveIndexProgress>? DriveProgressChanged;

    /// <summary>드라이브 단계/항목 수를 갱신하고 통지한다. itemCount&lt;0이면 기존 수를 유지한다.</summary>
    private void UpdateDrive(string root, DriveIndexPhase phase, long itemCount = -1)
    {
        var progress = _driveState.AddOrUpdate(
            root,
            _ => new DriveIndexProgress(root, phase, itemCount < 0 ? 0 : itemCount),
            (_, prev) => prev with { Phase = phase, ItemCount = itemCount < 0 ? prev.ItemCount : itemCount });
        DriveProgressChanged?.Invoke(this, progress);
    }

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
            var restored = _snapshot.TryLoad();
            if (restored is not null)
            {
                _catalog.Swap(restored);
                _logger.LogInformation("인덱스 스냅샷 복원: {Count:N0}개 항목 — 즉시 검색 가능", restored.Count);
            }

            var roots = ResolveRoots();
            if (roots.Count == 0)
            {
                Status = "인덱싱 대상 없음";
                return;
            }

            foreach (var pendingRoot in roots)
            {
                UpdateDrive(pendingRoot, DriveIndexPhase.Pending);
            }

            // 2) 스냅샷이 충분히 최신이면 전체 재스캔을 건너뛰고 FSW 증분만 시작(시작 CPU/IO 절감).
            //    아니면 새 인덱스에 전체 재구축 후 원자 교체(USN 볼륨은 자체 tailing, 나머지는 FSW 폴백).
            IReadOnlyList<string> watchRoots;
            if (ShouldSkipStartupRescan(restored))
            {
                _logger.LogInformation(
                    "스냅샷이 최신 — 로컬 전체 재스캔 생략, FSW 증분 감시만 시작 ({Count:N0}개 항목)",
                    _catalog.LastKnownCount);

                // 로컬은 스냅샷으로 이미 최신 → "생략"으로 표시. 네트워크는 아래에서 갱신한다.
                foreach (var localRoot in roots.Where(r => DriveKindOf(r) != DriveKind.Network))
                {
                    UpdateDrive(localRoot, DriveIndexPhase.Skipped);
                }

                // 로컬은 스냅샷으로 충분하지만, 네트워크 드라이브는 FSW 통지가 불안정하고 스냅샷 신선도 보장 밖이다.
                // 스킵해 버리면 FSW가 기존 파일을 열거하지 않아 네트워크가 영영 인덱싱되지 않으므로(핵심 버그),
                // 상한(노드 수·시간)을 두고 시작 시마다 살아있는 인덱스에 갱신한다.
                var networkRoots = roots.Where(r => DriveKindOf(r) == DriveKind.Network).ToList();
                if (networkRoots.Count > 0)
                {
                    using var lease = _catalog.Acquire();
                    await ScanNetworkRootsAsync(networkRoots, lease.Index, ct).ConfigureAwait(false);
                    _catalog.RefreshLastKnownCount();
                    _dirty = true;
                    SaveSnapshotIfDirty();
                }

                watchRoots = roots;
            }
            else
            {
                watchRoots = await RescanAllAsync(roots, ct).ConfigureAwait(false);
            }

            // 3) FSW 증분 감시 + 주기 스냅샷 (재스캔을 건너뛴 경우 모든 루트를 FSW로 감시)
            foreach (var root in watchRoots)
            {
                StartWatcher(root);
            }

            _snapshotTimer = new Timer(
                _ => SaveSnapshotIfDirty(), null, _options.SnapshotInterval, _options.SnapshotInterval);

            Status = $"감시 중 ({_catalog.LastKnownCount:N0}개 항목)";
            _logger.LogInformation("인덱싱 파이프라인 가동: {Status}", Status);

            // 재스캔 요청(FSW 오버플로) 처리 루프
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                await ProcessPendingRescansAsync(ct).ConfigureAwait(false);
                _catalog.RefreshLastKnownCount(); // 증분 변경(FSW/USN)을 반영해 표시용 카운트를 주기 갱신
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

    /// <summary>스냅샷이 충분히 최신이면 시작 전체 재스캔을 건너뛸지 판단한다.</summary>
    private bool ShouldSkipStartupRescan(FileIndex? restored)
    {
        // 진단/안전 밸브: 강제 전체 재스캔.
        if (Environment.GetEnvironmentVariable("EXPLORER_FORCE_FULL_RESCAN") == "1")
        {
            return false;
        }

        // 복원된 스냅샷이 없거나 비었으면 재스캔이 필요하다.
        if (restored is null || restored.Count == 0)
        {
            return false;
        }

        // USN 고속 경로는 전체 MFT 열거가 필요(스냅샷만으로 tailing 시작 불가) → 항상 재스캔.
        if (_options.FastIndexingEnabled)
        {
            return false;
        }

        // 옵션이 꺼져 있으면 스킵하지 않는다.
        if (_options.StartupRescanSkipMaxAge is not { } maxAge)
        {
            return false;
        }

        // 스냅샷 저장 시각(≈ 마지막 종료 시각)이 임계 이내일 때만 — 닫혀 있던 동안 변경이 적다고 본다.
        if (_snapshot.TryGetLastSavedUtc() is not { } savedUtc)
        {
            return false;
        }

        var age = DateTime.UtcNow - savedUtc;
        return age >= TimeSpan.Zero && age <= maxAge;
    }

    /// <summary>
    /// 전체 재구축. <b>로컬(고정) 드라이브를 먼저</b> 스캔·교체해 즉시 검색되게 하고,
    /// 네트워크 드라이브는 살아있는 인덱스에 상한(노드 수·시간)을 두고 추가한다(대용량 NAS가 앱을 막지 않게).
    /// FSW가 필요한 폴백 볼륨 목록을 돌려준다.
    /// </summary>
    private async Task<IReadOnlyList<string>> RescanAllAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        var rebuilt = new FileIndex();
        var fallbackRoots = new List<string>();
        var helperAvailable = _options.HelperPath is { } hp && UsnIndexSource.HelperExists(hp);

        var fixedRoots = roots.Where(r => DriveKindOf(r) != DriveKind.Network).ToList();
        var networkRoots = roots.Where(r => DriveKindOf(r) == DriveKind.Network).ToList();

        // 1) 로컬(고정) 드라이브 — USN 고속 또는 재귀 폴백.
        //    동시성: 앞 볼륨의 tailing(AddOrUpdate)과 뒤 볼륨의 열거(AddBatch)가 같은 rebuilt를
        //    동시에 쓸 수 있으나, FileIndex의 모든 변경이 쓰기 락을 잡아 직렬화된다(검색 읽기 락과도 안전).
        foreach (var root in fixedRoots)
        {
            var mode = IndexSourceSelector.Select(
                DriveKindOf(root),
                IndexSourceSelector.IsNtfsVolume(root),
                _options.FastIndexingEnabled,
                helperAvailable);

            if (mode == IndexSourceMode.UsnFast
                && await TryUsnRescanAsync(root, rebuilt, ct).ConfigureAwait(false))
            {
                continue;
            }

            await RecursiveRescanAsync(root, rebuilt, ct).ConfigureAwait(false);
            fallbackRoots.Add(root);
        }

        // 2) 로컬 인덱스를 먼저 원자 교체 — 네트워크 스캔을 기다리지 않고 여기서부터 로컬 검색이 동작한다.
        _catalog.Swap(rebuilt);
        _dirty = true;
        _logger.LogInformation("로컬 인덱스 재구축 완료: {Total:N0}개 항목 (USN {Usn}개 볼륨)",
            rebuilt.Count, _usnSources.Count);
        SaveSnapshotIfDirty();
        TrimMemory();

        // 3) 네트워크 드라이브는 살아있는 인덱스에 상한을 두고 추가(폭주·지연 방지).
        await ScanNetworkRootsAsync(networkRoots, rebuilt, ct).ConfigureAwait(false);
        fallbackRoots.AddRange(networkRoots);

        if (networkRoots.Count > 0)
        {
            _dirty = true;
            SaveSnapshotIfDirty();
            TrimMemory();
        }

        return fallbackRoots;
    }

    /// <summary>네트워크 루트들을 각각 상한(노드 수·시간) 안에서 인덱스에 추가한다. 시작 스킵 분기와 전체 재스캔이 공유.</summary>
    private async Task ScanNetworkRootsAsync(IReadOnlyList<string> networkRoots, FileIndex index, CancellationToken ct)
    {
        foreach (var root in networkRoots)
        {
            await RescanNetworkRootAsync(root, index, ct).ConfigureAwait(false);
        }
    }

    /// <summary>네트워크 드라이브를 노드 수·시간 상한을 두고 인덱스에 추가한다. 상한 초과 시 일부만(앱은 정상).</summary>
    private async Task RescanNetworkRootAsync(string root, FileIndex index, CancellationToken ct)
    {
        Status = $"네트워크 인덱싱: {root}";
        UpdateDrive(root, DriveIndexPhase.Scanning);
        _logger.LogInformation(
            "네트워크 드라이브 인덱싱 시작(상한 {Max:N0}개 / {Sec:N0}s): {Root}",
            _options.NetworkScanMaxItems, _options.NetworkScanTimeBudget.TotalSeconds, root);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(_options.NetworkScanTimeBudget);
        try
        {
            var total = await _scanner.ScanAsync(root, index.AddBatch, _options.NetworkScanMaxItems, budgetCts.Token)
                .ConfigureAwait(false);
            UpdateDrive(root, DriveIndexPhase.Watching, total);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 시간 예산 초과 — 일부만 인덱싱된 채 중단한다(로컬 검색은 이미 동작 중).
            UpdateDrive(root, DriveIndexPhase.Partial);
            _logger.LogWarning("네트워크 인덱싱 시간 예산 초과 — 일부만 인덱싱: {Root}", root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            UpdateDrive(root, DriveIndexPhase.Error);
            _logger.LogWarning(ex, "네트워크 인덱싱 실패 — 건너뜀: {Root}", root);
        }
    }

    /// <summary>대규모 재구축 직후 LOH 압축 + 워킹셋 트림으로 메모리를 OS에 반환한다(백그라운드 스레드에서만 호출).</summary>
    private void TrimMemory()
    {
        // 블로킹 압축 GC(blocking:true)는 UI 스레드를 포함한 모든 매니지드 스레드를 멈춰 끊김의 주원인이 되므로
        // 쓰지 않는다 — LOH 1회 압축 + 비블로킹 수집으로 끊김 없이 회수한다(워킹셋 반환은 다음 백그라운드 GC가 마무리).
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: true);
        ProcessMemory.TrimWorkingSet();
        _logger.LogDebug("인덱싱 후 메모리 트림 완료(비블로킹)");
    }

    /// <summary>USN 고속 경로 시도. 열거 성공 시 true(소스는 tailing 유지), 실패 시 false(호출자 폴백).</summary>
    private async Task<bool> TryUsnRescanAsync(string root, FileIndex rebuilt, CancellationToken ct)
    {
        Status = $"고속 인덱싱(USN): {root}";
        UpdateDrive(root, DriveIndexPhase.Scanning);
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
                UpdateDrive(root, DriveIndexPhase.Watching);
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
        UpdateDrive(root, DriveIndexPhase.Scanning);
        _logger.LogInformation("인덱스 스캔 시작(폴백): {Root}", root);
        try
        {
            var total = await _scanner.ScanAsync(root, rebuilt.AddBatch, ct).ConfigureAwait(false);
            UpdateDrive(root, DriveIndexPhase.Watching, total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            UpdateDrive(root, DriveIndexPhase.Error);
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
        FileIndexCatalog.Lease? lease = null;
        try
        {
            lease = _catalog.Acquire();
            var watcher = new FswIndexSource(
                lease.Index,
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
            _watchers.Add(new ActiveWatcher(watcher, lease));
            lease = null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            lease?.Dispose();
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
            using var lease = _catalog.Acquire();
            if (!_snapshot.TrySave(lease.Index))
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

    private sealed class ActiveWatcher(FswIndexSource source, FileIndexCatalog.Lease indexLease) : IDisposable
    {
        public void Dispose()
        {
            source.Dispose();
            indexLease.Dispose();
        }
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
