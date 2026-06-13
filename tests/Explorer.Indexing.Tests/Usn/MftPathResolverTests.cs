using Explorer.Indexing.Usn;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Usn;

public sealed class MftPathResolverTests
{
    private const ulong RootFrn = 5; // NTFS 루트 디렉터리

    private static MftRecord Dir(ulong frn, ulong parent, string name) => new(frn, parent, name, IsDirectory: true);

    private static MftRecord File(ulong frn, ulong parent, string name) => new(frn, parent, name, IsDirectory: false);

    [Fact]
    public void ResolvePath_FileUnderRoot_GivesVolumePath()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(File(100, RootFrn, "readme.txt"));

        resolver.ResolvePath(100).Should().Be(@"C:\readme.txt");
    }

    [Fact]
    public void ResolvePath_DeepChain_ReconstructsFullPath()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(Dir(10, RootFrn, "Users"));
        resolver.Add(Dir(20, 10, "박재현"));
        resolver.Add(Dir(30, 20, "문서"));
        resolver.Add(File(40, 30, "회의록.hwp"));

        resolver.ResolvePath(40).Should().Be(@"C:\Users\박재현\문서\회의록.hwp");
    }

    [Fact]
    public void ResolvePath_OrphanRecord_ReturnsNull()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(File(100, 999, "lost.txt")); // 부모 999가 수집 안 됨

        resolver.ResolvePath(100).Should().BeNull();
    }

    [Fact]
    public void ResolvePath_Cycle_IsBrokenAndReturnsNull()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        // 비정상: 10의 부모는 20, 20의 부모는 10 (순환)
        resolver.Add(Dir(10, 20, "a"));
        resolver.Add(Dir(20, 10, "b"));

        resolver.ResolvePath(10).Should().BeNull();
    }

    [Fact]
    public void ToIndexItems_EmitsResolvableRecordsWithParentPaths()
    {
        var resolver = new MftPathResolver(RootFrn, @"D:\");
        resolver.Add(Dir(10, RootFrn, "Work"));
        resolver.Add(File(20, 10, "plan.xlsx"));
        resolver.Add(File(30, RootFrn, "todo.txt"));

        var items = resolver.ToIndexItems().ToList();

        items.Should().Contain(i => i.ParentPath == @"D:\Work" && i.Name == "plan.xlsx" && !i.IsDirectory);
        items.Should().Contain(i => i.ParentPath == @"D:\" && i.Name == "todo.txt");
        items.Should().Contain(i => i.ParentPath == @"D:\" && i.Name == "Work" && i.IsDirectory);
    }

    [Fact]
    public void ToIndexItems_SkipsOrphans()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(File(20, 10, "child.txt")); // 부모 10 미수집 → 고아
        resolver.Add(File(30, RootFrn, "ok.txt"));

        var items = resolver.ToIndexItems().ToList();

        items.Should().ContainSingle().Which.Name.Should().Be("ok.txt");
    }

    [Fact]
    public void Add_RootRecordItself_IsIgnored()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(Dir(RootFrn, RootFrn, "."));

        resolver.RecordCount.Should().Be(0);
        resolver.ResolvePath(RootFrn).Should().Be(@"C:\");
    }

    [Fact]
    public void RenameSimulation_ChangingParent_UpdatesResolvedPath()
    {
        var resolver = new MftPathResolver(RootFrn, @"C:\");
        resolver.Add(Dir(10, RootFrn, "OldDir"));
        resolver.Add(File(20, 10, "file.txt"));

        resolver.ResolvePath(20).Should().Be(@"C:\OldDir\file.txt");
    }
}
