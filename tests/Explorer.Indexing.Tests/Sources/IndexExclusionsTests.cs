using Explorer.Indexing.Sources;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Sources;

public sealed class IndexExclusionsTests
{
    [Theory]
    [InlineData(@"C:\proj\app\node_modules", "node_modules")]
    [InlineData(@"C:\repo\.git", ".git")]
    [InlineData(@"C:\Windows\WinSxS", "WinSxS")]
    [InlineData(@"C:\$Recycle.Bin", "$Recycle.Bin")]
    [InlineData(@"D:\System Volume Information", "System Volume Information")]
    public void IsExcludedDirectory_ByName_Excludes(string fullPath, string name) =>
        IndexExclusions.IsExcludedDirectory(fullPath, name).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\Windows\Installer\abc", "abc")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\x", "x")]
    [InlineData(@"C:\Users\me\.nuget\packages\foo", "foo")]
    [InlineData(@"C:\ProgramData\Package Cache\bar", "bar")]
    public void IsExcludedDirectory_ByFragment_Excludes(string fullPath, string name) =>
        IndexExclusions.IsExcludedDirectory(fullPath, name).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\Users\me\Documents", "Documents")]
    [InlineData(@"C:\repo\src", "src")]
    [InlineData(@"C:\Program Files\App", "App")]
    [InlineData(@"C:\Users\me\Downloads", "Downloads")]
    public void IsExcludedDirectory_NormalFolders_NotExcluded(string fullPath, string name) =>
        IndexExclusions.IsExcludedDirectory(fullPath, name).Should().BeFalse();

    [Theory]
    [InlineData(@"C:\proj\node_modules\pkg\index.js")]
    [InlineData(@"C:\repo\.git\objects\ab\cd")]
    [InlineData(@"C:\Users\me\AppData\Local\Packages\app\file.dat")]
    [InlineData(@"C:\Windows\WinSxS\amd64_foo\bar.dll")]
    public void IsExcludedPath_UnderExcludedTree_Excludes(string fullPath) =>
        IndexExclusions.IsExcludedPath(fullPath).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\Users\me\Documents\report.docx")]
    [InlineData(@"C:\repo\src\main.cs")]
    [InlineData(@"C:\Program Files\App\app.exe")]
    public void IsExcludedPath_NormalFile_NotExcluded(string fullPath) =>
        IndexExclusions.IsExcludedPath(fullPath).Should().BeFalse();
}
