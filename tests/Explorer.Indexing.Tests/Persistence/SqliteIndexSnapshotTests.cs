using Explorer.Indexing.Index;
using Explorer.Indexing.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Indexing.Tests.Persistence;

public sealed class SqliteIndexSnapshotTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SqliteIndexSnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "index.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private SqliteIndexSnapshot CreateSnapshot() =>
        new(_dbPath, NullLogger<SqliteIndexSnapshot>.Instance);

    [Fact]
    public void TryLoad_NoFile_GivesNull()
    {
        CreateSnapshot().TryLoad().Should().BeNull();
    }

    [Fact]
    public void SaveAndLoad_RoundtripsIndexContent()
    {
        using var index = new FileIndex();
        index.AddOrUpdate(new IndexItem(@"C:\문서", "회의록.hwp", false, 123, new DateTime(2026, 2, 3).Ticks));
        index.AddOrUpdate(new IndexItem(@"C:\문서\하위", "deep.txt", false, 45, new DateTime(2026, 2, 4).Ticks));
        index.AddOrUpdate(new IndexItem(@"D:\data", "video.mp4", false, 9_000_000, new DateTime(2026, 2, 5).Ticks));

        CreateSnapshot().TrySave(index).Should().BeTrue();

        using var restored = CreateSnapshot().TryLoad();
        restored.Should().NotBeNull();
        restored!.Search("회의록", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\문서\회의록.hwp");
        restored.Search("deep", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\문서\하위\deep.txt");
        var video = restored.Search("video", 10).Should().ContainSingle().Subject;
        video.Size.Should().Be(9_000_000);
        video.Modified.Should().Be(new DateTime(2026, 2, 5));
    }

    [Fact]
    public void Save_Twice_ReplacesPreviousContent()
    {
        using var first = new FileIndex();
        first.AddOrUpdate(new IndexItem(@"C:\a", "old.txt", false, 1, 0));
        CreateSnapshot().TrySave(first).Should().BeTrue();

        using var second = new FileIndex();
        second.AddOrUpdate(new IndexItem(@"C:\a", "new.txt", false, 1, 0));
        CreateSnapshot().TrySave(second).Should().BeTrue();

        using var restored = CreateSnapshot().TryLoad();
        restored!.Search("old", 10).Should().BeEmpty();
        restored.Search("new", 10).Should().ContainSingle();
    }

    [Fact]
    public void TryLoad_CorruptFile_GivesNullWithoutThrowing()
    {
        File.WriteAllText(_dbPath, "this is not a sqlite database at all");

        CreateSnapshot().TryLoad().Should().BeNull();
    }

    [Fact]
    public void TrySave_ToInvalidLocation_ReturnsFalseWithoutThrowing()
    {
        using var index = new FileIndex();
        index.AddOrUpdate(new IndexItem(@"C:\a", "x.txt", false, 1, 0));
        var snapshot = new SqliteIndexSnapshot(
            Path.Combine(_tempDir, "no\0pe", "bad.db"), NullLogger<SqliteIndexSnapshot>.Instance);

        snapshot.TrySave(index).Should().BeFalse();
    }
}
