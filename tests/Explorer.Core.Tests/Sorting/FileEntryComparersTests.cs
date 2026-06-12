using Explorer.Core.FileSystem;
using Explorer.Core.Sorting;
using FluentAssertions;

namespace Explorer.Core.Tests.Sorting;

public sealed class FileEntryComparersTests
{
    private static FileEntry File(string name, long size = 0, int modifiedDay = 1) => FileEntry.Create(
        @"C:\t\" + name, name, isDirectory: false, size,
        new DateTime(2026, 1, modifiedDay), new DateTime(2026, 1, 1), FileAttributes.Normal);

    private static FileEntry Dir(string name) => FileEntry.Create(
        @"C:\t\" + name, name, isDirectory: true, 0,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), FileAttributes.Directory);

    [Fact]
    public void Sort_ByName_FoldersAlwaysFirst()
    {
        var items = new List<FileEntry> { File("a.txt"), Dir("zfolder"), File("b.txt"), Dir("afolder") };

        items.Sort(FileEntryComparers.Create(SortDescriptor.Default));

        items.Select(i => i.Name).Should().Equal("afolder", "zfolder", "a.txt", "b.txt");
    }

    [Fact]
    public void Sort_ByNameDescending_KeepsFoldersFirst()
    {
        var items = new List<FileEntry> { File("a.txt"), Dir("zfolder"), File("b.txt"), Dir("afolder") };

        items.Sort(FileEntryComparers.Create(new SortDescriptor(SortColumn.Name, Descending: true)));

        items.Select(i => i.Name).Should().Equal("zfolder", "afolder", "b.txt", "a.txt");
    }

    [Fact]
    public void Sort_ByName_UsesNaturalOrder()
    {
        var items = new List<FileEntry> { File("file10.txt"), File("file2.txt") };

        items.Sort(FileEntryComparers.Create(SortDescriptor.Default));

        items.Select(i => i.Name).Should().Equal("file2.txt", "file10.txt");
    }

    [Fact]
    public void Sort_BySize_TieBreaksByName()
    {
        var items = new List<FileEntry> { File("b.txt", size: 10), File("c.txt", size: 5), File("a.txt", size: 10) };

        items.Sort(FileEntryComparers.Create(new SortDescriptor(SortColumn.Size, Descending: false)));

        items.Select(i => i.Name).Should().Equal("c.txt", "a.txt", "b.txt");
    }

    [Fact]
    public void Sort_ByDateModified_Works()
    {
        var items = new List<FileEntry> { File("new.txt", modifiedDay: 20), File("old.txt", modifiedDay: 2) };

        items.Sort(FileEntryComparers.Create(new SortDescriptor(SortColumn.DateModified, Descending: true)));

        items.Select(i => i.Name).Should().Equal("new.txt", "old.txt");
    }

    [Fact]
    public void Sort_ByExtension_GroupsByExtensionThenName()
    {
        var items = new List<FileEntry> { File("b.zip"), File("a.txt"), File("a.zip") };

        items.Sort(FileEntryComparers.Create(new SortDescriptor(SortColumn.Extension, Descending: false)));

        items.Select(i => i.Name).Should().Equal("a.txt", "a.zip", "b.zip");
    }

    [Fact]
    public void Toggle_SameColumnFlipsDirection_NewColumnResetsAscending()
    {
        var sort = SortDescriptor.Default;

        var flipped = sort.Toggle(SortColumn.Name);
        flipped.Should().Be(new SortDescriptor(SortColumn.Name, Descending: true));

        var switched = flipped.Toggle(SortColumn.Size);
        switched.Should().Be(new SortDescriptor(SortColumn.Size, Descending: false));
    }
}
