using Explorer.Core.FileSystem;
using Explorer.Indexing;
using Explorer.Indexing.Index;
using Explorer.Indexing.Persistence;
using Explorer.Indexing.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.Indexing.Tests;

/// <summary>실제 파이프라인 통합: 스캔 → 교체 → FSW 증분 → 스냅샷 저장/복원.</summary>
public sealed class IndexingServiceTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly string _rootDir;
    private readonly string _dbPath;
    private readonly FileIndexCatalog _catalog = new();
    private readonly IndexingService _service;

    public IndexingServiceTests()
    {
        _tempDir = Path.Combine(AppContext.BaseDirectory, "IndexingServiceTests", Guid.NewGuid().ToString("N"));
        _rootDir = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(_rootDir);
        _dbPath = Path.Combine(_tempDir, "index.db");

        File.WriteAllText(Path.Combine(_rootDir, "seed.txt"), "initial");

        _service = new IndexingService(
            _catalog,
            new SqliteIndexSnapshot(_dbPath, NullLogger<SqliteIndexSnapshot>.Instance),
            new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance),
            Substitute.For<IDriveProvider>(),
            new IndexingOptions { Roots = [_rootDir], SnapshotInterval = TimeSpan.FromHours(1) },
            NullLogger<IndexingService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        condition().Should().BeTrue(because);
    }

    private static IDriveProvider DriveProviderFor(string root, DriveKind kind)
    {
        var provider = Substitute.For<IDriveProvider>();
        provider.GetDrives().Returns([
            new DriveEntry(Path.GetPathRoot(root)!, string.Empty, kind, 0, 0, IsReady: true),
        ]);
        return provider;
    }

    [Fact]
    public async Task Pipeline_ScansRoot_ThenWatchesIncrementally_AndPersistsSnapshot()
    {
        await _service.StartAsync(CancellationToken.None);

        // 1) 초기 스캔 완료 → 검색 가능
        await WaitUntilAsync(
            () => _catalog.Current.Search("seed", 5).Count == 1,
            "초기 스캔이 끝나면 기존 파일이 검색돼야 함");

        // 2) 스냅샷 파일 생성됨
        await WaitUntilAsync(() => File.Exists(_dbPath), "재구축 후 스냅샷 저장");

        // 3) FSW 증분: 새 파일이 실시간 반영. 부하가 큰 머신에선 FSW 이벤트가 지연/유실될 수 있어
        //    반영될 때까지 파일을 반복 재기록(Created/Changed 이벤트를 거듭 유발)하며 기다린다.
        var liveFile = Path.Combine(_rootDir, "live-update.txt");
        var indexed = false;
        for (var i = 0; i < 600 && !indexed; i++)
        {
            File.WriteAllText(liveFile, "fresh " + i);
            await Task.Delay(25);
            indexed = _catalog.Current.Search("live-update", 5).Count == 1;
        }

        indexed.Should().BeTrue("감시 단계에서 새 파일이 인덱스에 반영");

        // 4) 종료 시 dirty 스냅샷 저장 → 새 서비스가 스냅샷만으로 즉시 검색 가능
        await _service.StopAsync(CancellationToken.None);

        var catalog2 = new FileIndexCatalog();
        var snapshot2 = new SqliteIndexSnapshot(_dbPath, NullLogger<SqliteIndexSnapshot>.Instance);
        using var restored = snapshot2.TryLoad();
        restored.Should().NotBeNull();
        restored!.Search("live-update", 5).Should().ContainSingle("증분 변경이 스냅샷에 포함");
        _ = catalog2;
    }

    [Fact]
    public async Task FreshSnapshot_SkipsStartupRescan_RestoresWithoutScanningDisk()
    {
        // 디스크엔 seed.txt가 있지만, 스냅샷엔 그와 다른 snapshot-only.txt만 넣어 둔다.
        var snapshotPath = Path.Combine(_tempDir, "skip.db");
        using (var seeded = new FileIndex())
        {
            seeded.AddOrUpdate(new IndexItem(_rootDir, "snapshot-only.txt", IsDirectory: false, 7, 0));
            new SqliteIndexSnapshot(snapshotPath, NullLogger<SqliteIndexSnapshot>.Instance).TrySave(seeded);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var catalog = new FileIndexCatalog();
        var service = new IndexingService(
            catalog,
            new SqliteIndexSnapshot(snapshotPath, NullLogger<SqliteIndexSnapshot>.Instance),
            new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance),
            Substitute.For<IDriveProvider>(),
            new IndexingOptions
            {
                Roots = [_rootDir],
                SnapshotInterval = TimeSpan.FromHours(1),
                StartupRescanSkipMaxAge = TimeSpan.FromHours(6),
            },
            NullLogger<IndexingService>.Instance);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(
                () => catalog.Current.Search("snapshot-only", 5).Count == 1, "스냅샷이 즉시 복원");
            await Task.Delay(150); // 재스캔이 있었다면 seed.txt가 들어올 시간

            catalog.Current.Search("snapshot-only", 5).Should().ContainSingle();
            catalog.Current.Search("seed", 5).Should()
                .BeEmpty("최신 스냅샷이라 시작 재스캔을 건너뛰어 디스크의 seed.txt는 인덱싱되지 않음");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    [Fact]
    public async Task DriveProgressSubscriberException_DoesNotStopPipeline()
    {
        _service.DriveProgressChanged += (_, _) => throw new InvalidOperationException("bad subscriber");

        await _service.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => _catalog.Current.Search("seed", 5).Count == 1,
            "progress subscribers cannot stop indexing");
        _service.Status.Should().NotBe("오류");
    }

    [Fact]
    public async Task FreshSnapshot_NetworkRoot_ReplacesStaleSubtree()
    {
        var snapshotPath = Path.Combine(_tempDir, "network-skip.db");
        File.WriteAllText(Path.Combine(_rootDir, "keep-network.txt"), "keep");
        using (var seeded = new FileIndex())
        {
            seeded.AddOrUpdate(new IndexItem(_rootDir, "keep-network.txt", IsDirectory: false, 4, 0));
            seeded.AddOrUpdate(new IndexItem(_rootDir, "stale-network.txt", IsDirectory: false, 5, 0));
            new SqliteIndexSnapshot(snapshotPath, NullLogger<SqliteIndexSnapshot>.Instance).TrySave(seeded)
                .Should().BeTrue();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var catalog = new FileIndexCatalog();
        var service = new IndexingService(
            catalog,
            new SqliteIndexSnapshot(snapshotPath, NullLogger<SqliteIndexSnapshot>.Instance),
            new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance),
            DriveProviderFor(_rootDir, DriveKind.Network),
            new IndexingOptions
            {
                Roots = [_rootDir],
                StartupRescanSkipMaxAge = TimeSpan.FromHours(6),
                SnapshotInterval = TimeSpan.FromHours(1),
                NetworkScanMaxItems = 1000,
                NetworkScanTimeBudget = TimeSpan.FromSeconds(5),
            },
            NullLogger<IndexingService>.Instance);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(
                () => catalog.Current.Search("keep-network", 5).Count == 1
                    && catalog.Current.Search("stale-network", 5).Count == 0,
                "complete network refresh replaces stale snapshot entries");

            service.DriveProgress.Single(p => p.Root == _rootDir).Phase.Should().Be(DriveIndexPhase.Watching);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    [Fact]
    public async Task NetworkRoot_HittingMaxItems_IsMarkedPartial()
    {
        var networkRoot = Path.Combine(_tempDir, "network-cap");
        Directory.CreateDirectory(networkRoot);
        for (var i = 0; i < 30; i++)
        {
            File.WriteAllText(Path.Combine(networkRoot, $"file{i:D2}.txt"), "x");
        }

        var catalog = new FileIndexCatalog();
        var service = new IndexingService(
            catalog,
            new SqliteIndexSnapshot(Path.Combine(_tempDir, "network-cap.db"), NullLogger<SqliteIndexSnapshot>.Instance),
            new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance),
            DriveProviderFor(networkRoot, DriveKind.Network),
            new IndexingOptions
            {
                Roots = [networkRoot],
                SnapshotInterval = TimeSpan.FromHours(1),
                NetworkScanMaxItems = 10,
                NetworkScanTimeBudget = TimeSpan.FromSeconds(5),
            },
            NullLogger<IndexingService>.Instance);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(
                () => service.DriveProgress.Any(p => p.Root == networkRoot && p.Phase == DriveIndexPhase.Partial),
                "network item cap is surfaced as partial");

            var indexed = catalog.Current.Search("file", 50).Count;
            indexed.Should().BeGreaterThan(0);
            indexed.Should().BeLessThan(30);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    [Fact]
    public async Task DisabledOption_DoesNothing()
    {
        var disabled = new IndexingService(
            new FileIndexCatalog(),
            new SqliteIndexSnapshot(Path.Combine(_tempDir, "x.db"), NullLogger<SqliteIndexSnapshot>.Instance),
            new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance),
            Substitute.For<IDriveProvider>(),
            new IndexingOptions { Disabled = true },
            NullLogger<IndexingService>.Instance);

        await disabled.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await disabled.StopAsync(CancellationToken.None);
        disabled.Dispose();

        File.Exists(Path.Combine(_tempDir, "x.db")).Should().BeFalse();
    }
}
