using Explorer.Core.FileOperations;
using FluentAssertions;

namespace Explorer.Core.Tests.FileOperations;

public sealed class FileNameRulesTests
{
    [Theory]
    [InlineData("문서.txt")]
    [InlineData("new folder")]
    [InlineData("a.b.c")]
    [InlineData(".gitignore")]
    public void IsValid_AcceptsNormalNames(string name)
    {
        FileNameRules.IsValid(name, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    [InlineData("bad?name")]
    [InlineData("bad\"name")]
    [InlineData("bad<name")]
    [InlineData("bad|name")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData("COM1")]
    [InlineData("LPT9.log")]
    [InlineData("NUL")]
    public void IsValid_RejectsInvalidNames(string? name)
    {
        FileNameRules.IsValid(name, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsValid_RejectsTooLongNames()
    {
        FileNameRules.IsValid(new string('a', 256), out _).Should().BeFalse();
        FileNameRules.IsValid(new string('a', 255), out _).Should().BeTrue();
    }

    [Fact]
    public void GenerateUniqueName_ReturnsBaseWhenFree()
    {
        FileNameRules.GenerateUniqueName(["기존"], "새 폴더").Should().Be("새 폴더");
    }

    [Fact]
    public void GenerateUniqueName_AppendsCounterUntilFree()
    {
        var existing = new[] { "새 폴더", "새 폴더 (2)", "새 폴더 (3)" };

        FileNameRules.GenerateUniqueName(existing, "새 폴더").Should().Be("새 폴더 (4)");
    }

    [Fact]
    public void GenerateUniqueName_IsCaseInsensitive()
    {
        FileNameRules.GenerateUniqueName(["NEW FOLDER"], "new folder").Should().Be("new folder (2)");
    }
}
