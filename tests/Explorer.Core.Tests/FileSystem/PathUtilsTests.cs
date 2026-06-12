using Explorer.Core.FileSystem;
using FluentAssertions;

namespace Explorer.Core.Tests.FileSystem;

public sealed class PathUtilsTests
{
    [Theory]
    [InlineData(@"C:\Users", true)]
    [InlineData(@"\\server\share\folder", true)]
    [InlineData(@"relative\path", false)]
    [InlineData(@"\rootless", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsAbsolutePath_DetectsFullyQualifiedPaths(string? path, bool expected)
    {
        PathUtils.IsAbsolutePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\", @"C:\Users")]
    [InlineData(@"  C:\Users\Public  ", @"C:\Users\Public")]
    [InlineData(@"C:\Users\..\Windows", @"C:\Windows")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Users/Public", @"C:\Users\Public")]
    public void Normalize_CleansUpPath(string input, string expected)
    {
        PathUtils.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"relative\path")]
    [InlineData("just-a-name")]
    [InlineData("   ")]
    public void Normalize_RejectsNonAbsolutePaths(string input)
    {
        var act = () => PathUtils.Normalize(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetParent_WalksUpToRootThenNull()
    {
        PathUtils.GetParent(@"C:\Users\Public").Should().Be(@"C:\Users");
        PathUtils.GetParent(@"C:\Users").Should().Be(@"C:\");
        PathUtils.GetParent(@"C:\").Should().BeNull();
    }

    [Fact]
    public void GetParent_UncShareRoot_ReturnsNull()
    {
        PathUtils.GetParent(@"\\server\share").Should().BeNull();
    }
}
