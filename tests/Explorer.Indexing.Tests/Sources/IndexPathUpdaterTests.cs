using Explorer.Indexing.Index;
using Explorer.Indexing.Sources;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Sources;

public sealed class IndexPathUpdaterTests : IDisposable
{
    private readonly string _root;

    public IndexPathUpdaterTests()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "IndexPathUpdaterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void FilterExcluded_RemovesItemsUnderExcludedTrees()
    {
        var items = new[]
        {
            new IndexItem(@"C:\repo\node_modules\pkg", "index.js", false, 1, 2),
            new IndexItem(@"C:\repo\.git", "config", false, 1, 2),
            new IndexItem(@"C:\repo\src", "app.cs", false, 1, 2),
        };

        var filtered = IndexPathUpdater.FilterExcluded(items);

        filtered.Should().ContainSingle()
            .Which.Name.Should().Be("app.cs");
    }

    [Fact]
    public void AddExistingPathTree_Directory_AddsDescendants()
    {
        var moved = Path.Combine(_root, "Moved");
        var nested = Path.Combine(moved, "Nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "child.txt"), "hello");
        using var index = new FileIndex();

        IndexPathUpdater.AddExistingPathTree(index, moved, isDirectoryHint: true);

        index.Search("Moved", 10).Should().ContainSingle().Which.IsDirectory.Should().BeTrue();
        index.Search("Nested", 10).Should().ContainSingle().Which.IsDirectory.Should().BeTrue();
        var child = index.Search("child", 10).Should().ContainSingle().Subject;
        child.FullPath.Should().Be(Path.Combine(nested, "child.txt"));
        child.Size.Should().Be(5);
    }

    [Fact]
    public void AddSinglePath_Directory_DoesNotAddDescendants()
    {
        var dir = Path.Combine(_root, "Folder");
        var nested = Path.Combine(dir, "Nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(dir, "child.txt"), "x");
        using var index = new FileIndex();

        IndexPathUpdater.AddSinglePath(index, dir, isDirectoryHint: true);

        index.Search("Folder", 10).Should().ContainSingle().Which.IsDirectory.Should().BeTrue();
        index.Search("Nested", 10).Should().BeEmpty("단일 경로 반영은 하위를 열거하지 않는다");
        index.Search("child", 10).Should().BeEmpty();
    }

    [Fact]
    public void AddSinglePath_MissingPath_RemovesExistingEntry()
    {
        var path = Path.Combine(_root, "ghost.txt");
        using var index = new FileIndex();
        IndexPathUpdater.AddKnownPath(index, path, isDirectory: false);

        var updated = IndexPathUpdater.AddSinglePath(index, path, isDirectoryHint: false);

        updated.Should().BeFalse();
        index.Search("ghost", 10).Should().BeEmpty("늦은 Changed 이벤트가 삭제된 파일을 되살리면 안 된다");
    }

    [Fact]
    public void AddExistingPathTree_MissingPath_RemovesExistingTree()
    {
        var dir = Path.Combine(_root, "gone");
        using var index = new FileIndex();
        IndexPathUpdater.AddKnownPath(index, dir, isDirectory: true);
        IndexPathUpdater.AddKnownPath(index, Path.Combine(dir, "child.txt"), isDirectory: false);

        var updated = IndexPathUpdater.AddExistingPathTree(index, dir, isDirectoryHint: true);

        updated.Should().BeFalse();
        index.Search("gone", 10).Should().BeEmpty();
        index.Search("child", 10).Should().BeEmpty();
    }

    [Fact]
    public void AddKnownPath_MissingPath_CanSeedTrustedEventPath()
    {
        var path = Path.Combine(_root, "known-from-journal.txt");
        using var index = new FileIndex();

        IndexPathUpdater.AddKnownPath(index, path, isDirectory: false);

        index.Search("known-from-journal", 10).Should().ContainSingle();
    }

    [Fact]
    public void AddExistingPathTree_ExcludedDirectory_DoesNotIndexTree()
    {
        var ignored = Path.Combine(_root, "node_modules");
        Directory.CreateDirectory(ignored);
        File.WriteAllText(Path.Combine(ignored, "package.js"), "x");
        using var index = new FileIndex();

        IndexPathUpdater.AddExistingPathTree(index, ignored, isDirectoryHint: true);

        index.Search("node_modules", 10).Should().BeEmpty();
        index.Search("package", 10).Should().BeEmpty();
    }
}
