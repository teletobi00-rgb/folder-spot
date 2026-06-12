using Explorer.Core.FileSystem;
using Explorer.Shell.Icons;
using FluentAssertions;

namespace Explorer.Shell.Tests.Icons;

public sealed class IconCacheKeyTests
{
    private static FileEntry Entry(string name, bool isDirectory = false) => FileEntry.Create(
        @"C:\t\" + name, name, isDirectory, 0, default, default,
        isDirectory ? FileAttributes.Directory : FileAttributes.Normal);

    [Fact]
    public void For_Directory_UsesSharedFolderKey()
    {
        IconCacheKey.For(Entry("folderA", isDirectory: true))
            .Should().Be(IconCacheKey.For(Entry("folderB", isDirectory: true)));
    }

    [Fact]
    public void For_RegularFiles_GroupedByExtension()
    {
        IconCacheKey.For(Entry("a.txt")).Should().Be(IconCacheKey.For(Entry("b.txt")));
        IconCacheKey.For(Entry("a.txt")).Should().NotBe(IconCacheKey.For(Entry("a.zip")));
    }

    [Theory]
    [InlineData("app.exe")]
    [InlineData("shortcut.lnk")]
    [InlineData("favicon.ico")]
    public void For_PerPathKinds_KeyedByFullPath(string name)
    {
        var key1 = IconCacheKey.For(Entry(name));
        var entry2 = FileEntry.Create(@"D:\other\" + name, name, false, 0, default, default, FileAttributes.Normal);

        key1.Should().NotBe(IconCacheKey.For(entry2));
        IconCacheKey.IsExtensionScoped(Entry(name)).Should().BeFalse();
    }

    [Fact]
    public void For_NoExtension_UsesSharedKey()
    {
        IconCacheKey.For(Entry("README")).Should().Be(IconCacheKey.For(Entry("LICENSE")));
    }

    [Fact]
    public void IsExtensionScoped_TrueForRegularFiles_FalseForDirectories()
    {
        IconCacheKey.IsExtensionScoped(Entry("a.txt")).Should().BeTrue();
        IconCacheKey.IsExtensionScoped(Entry("folder", isDirectory: true)).Should().BeFalse();
    }
}
