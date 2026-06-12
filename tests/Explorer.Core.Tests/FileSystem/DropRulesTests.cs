using Explorer.Core.FileSystem;
using FluentAssertions;

namespace Explorer.Core.Tests.FileSystem;

public sealed class DropRulesTests
{
    [Theory]
    [InlineData(true, false, true, DropOperation.Copy)]   // Ctrl 우선
    [InlineData(false, true, false, DropOperation.Move)]  // Shift 우선
    [InlineData(false, false, true, DropOperation.Move)]  // 같은 볼륨 기본=이동
    [InlineData(false, false, false, DropOperation.Copy)] // 다른 볼륨 기본=복사
    public void Resolve_FollowsExplorerConventions(bool ctrl, bool shift, bool sameVolume, DropOperation expected)
    {
        DropRules.Resolve(ctrl, shift, sameVolume).Should().Be(expected);
    }

    [Fact]
    public void IsSameVolume_ComparesRoots()
    {
        DropRules.IsSameVolume(@"C:\a\b", @"c:\x").Should().BeTrue();
        DropRules.IsSameVolume(@"C:\a", @"D:\a").Should().BeFalse();
    }

    [Fact]
    public void CanDrop_RejectsDropOntoSelf()
    {
        DropRules.CanDrop([@"C:\data\folder"], @"C:\data\folder", DropOperation.Copy).Should().BeFalse();
    }

    [Fact]
    public void CanDrop_RejectsDropIntoOwnSubtree()
    {
        DropRules.CanDrop([@"C:\data\folder"], @"C:\data\folder\sub\deep", DropOperation.Move).Should().BeFalse();
    }

    [Fact]
    public void CanDrop_RejectsMoveToSameParent()
    {
        DropRules.CanDrop([@"C:\data\a.txt", @"C:\data\b.txt"], @"C:\data", DropOperation.Move).Should().BeFalse();
    }

    [Fact]
    public void CanDrop_AllowsCopyToSameParent()
    {
        DropRules.CanDrop([@"C:\data\a.txt"], @"C:\data", DropOperation.Copy).Should().BeTrue();
    }

    [Fact]
    public void CanDrop_AllowsNormalMove()
    {
        DropRules.CanDrop([@"C:\data\a.txt"], @"D:\backup", DropOperation.Move).Should().BeTrue();
    }

    [Fact]
    public void CanDrop_RejectsNoneAndEmpty()
    {
        DropRules.CanDrop([@"C:\a"], @"C:\b", DropOperation.None).Should().BeFalse();
        DropRules.CanDrop([], @"C:\b", DropOperation.Copy).Should().BeFalse();
    }

    [Fact]
    public void CanDrop_SimilarPrefixFolder_IsNotTreatedAsDescendant()
    {
        // "C:\data" 와 "C:\data2"는 접두사만 비슷할 뿐 부모-자식이 아니다
        DropRules.CanDrop([@"C:\data"], @"C:\data2", DropOperation.Move).Should().BeTrue();
    }
}
