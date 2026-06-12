using Explorer.Core.FileSystem;
using FluentAssertions;

namespace Explorer.Core.Tests.FileSystem;

public sealed class FileEntryTests
{
    private static FileEntry CreateFile(string name) => FileEntry.Create(
        fullPath: @"C:\temp\" + name,
        name: name,
        isDirectory: false,
        size: 100,
        dateModified: new DateTime(2026, 1, 1),
        dateCreated: new DateTime(2026, 1, 1),
        attributes: FileAttributes.Normal);

    [Theory]
    [InlineData("readme.txt", "txt")]
    [InlineData("README.TXT", "txt")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("noextension", "")]
    [InlineData("endswithdot.", "")]
    [InlineData(".gitignore", "gitignore")]
    public void Create_NormalizesExtension_LowercaseWithoutDot(string name, string expected)
    {
        CreateFile(name).Extension.Should().Be(expected);
    }

    [Fact]
    public void Create_ForDirectory_HasEmptyExtensionAndZeroSize()
    {
        var entry = FileEntry.Create(
            @"C:\temp\folder.v2", "folder.v2", isDirectory: true, size: 999,
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), FileAttributes.Directory);

        entry.Extension.Should().BeEmpty();
        entry.Size.Should().Be(0);
        entry.IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void HiddenAndSystemFlags_AreDerivedFromAttributes()
    {
        var entry = FileEntry.Create(
            @"C:\temp\desktop.ini", "desktop.ini", isDirectory: false, size: 1,
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 1),
            FileAttributes.Hidden | FileAttributes.System);

        entry.IsHidden.Should().BeTrue();
        entry.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void Create_WithBlankNameOrPath_Throws()
    {
        var actName = () => FileEntry.Create(@"C:\a", " ", false, 0, default, default, default);
        var actPath = () => FileEntry.Create("", "a.txt", false, 0, default, default, default);

        actName.Should().Throw<ArgumentException>();
        actPath.Should().Throw<ArgumentException>();
    }
}
