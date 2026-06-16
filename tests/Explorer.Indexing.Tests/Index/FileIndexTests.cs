using Explorer.Indexing.Index;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Index;

public sealed class FileIndexTests : IDisposable
{
    private readonly FileIndex _index = new();

    public void Dispose() => _index.Dispose();

    private void Add(string parentPath, string name, bool isDir = false, long size = 10) =>
        _index.AddOrUpdate(new IndexItem(parentPath, name, isDir, size, new DateTime(2026, 1, 1).Ticks));

    [Fact]
    public void AddAndSearch_FindsBySubstring_CaseInsensitive()
    {
        Add(@"C:\Docs", "Report-Final.pdf");
        Add(@"C:\Docs", "notes.txt");

        var hits = _index.Search("final", 10);

        hits.Should().ContainSingle().Which.FullPath.Should().Be(@"C:\Docs\Report-Final.pdf");
    }

    [Fact]
    public void Search_KoreanSubstring_Matches()
    {
        Add(@"C:\문서", "회의록_2026.hwp");
        Add(@"C:\문서", "보고서.docx");

        _index.Search("회의", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\문서\회의록_2026.hwp");
        _index.Search("보고서", 10).Should().ContainSingle();
        _index.Search("없는말", 10).Should().BeEmpty();
    }

    [Fact]
    public void Search_NormalizesComposedQueryAgainstDecomposedName()
    {
        Add(@"C:\문서", "\u1100\u1161.txt");

        _index.Search("\uAC00", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\문서\가.txt");
    }

    [Fact]
    public void Search_MixedKoreanEnglish_Matches()
    {
        Add(@"C:\작업", "Project계획서_v2.xlsx");

        _index.Search("project계획", 10).Should().ContainSingle();
        _index.Search("PROJECT", 10).Should().ContainSingle();
    }

    [Fact]
    public void Search_RanksExactThenPrefixThenSubstring()
    {
        Add(@"C:\a", "log");
        Add(@"C:\a", "logger.cs");
        Add(@"C:\a", "catalog.txt");

        var hits = _index.Search("log", 10);

        hits.Select(h => h.Name).Should().Equal("log", "logger.cs", "catalog.txt");
    }

    [Fact]
    public void Search_RespectsMaxResults()
    {
        for (var i = 0; i < 20; i++)
        {
            Add(@"C:\many", $"file{i:00}.txt");
        }

        _index.Search("file", 5).Should().HaveCount(5);
    }

    [Fact]
    public void Search_EmptyOrWhitespaceQuery_GivesEmpty()
    {
        Add(@"C:\a", "x.txt");

        _index.Search("", 10).Should().BeEmpty();
        _index.Search("   ", 10).Should().BeEmpty();
    }

    [Fact]
    public void DirectoriesAreSearchableToo()
    {
        Add(@"C:\", "Projects", isDir: true);
        Add(@"C:\Projects", "readme.md");

        var hits = _index.Search("Projects", 10);

        hits.Should().Contain(h => h.IsDirectory && h.FullPath == @"C:\Projects");
    }

    [Fact]
    public void AddOrUpdate_SamePath_UpdatesMetadataWithoutDuplicating()
    {
        Add(@"C:\a", "f.txt", size: 10);
        Add(@"C:\a", "f.txt", size: 999);

        var hits = _index.Search("f.txt", 10);

        hits.Should().ContainSingle().Which.Size.Should().Be(999);
    }

    [Fact]
    public void RemoveSubtree_File_RemovesIt()
    {
        Add(@"C:\a", "gone.txt");

        _index.RemoveSubtree(@"C:\a\gone.txt");

        _index.Search("gone", 10).Should().BeEmpty();
    }

    [Fact]
    public void RemoveSubtree_Directory_RemovesAllDescendants()
    {
        Add(@"C:\root", "sub", isDir: true);
        Add(@"C:\root\sub", "deep", isDir: true);
        Add(@"C:\root\sub\deep", "leaf.txt");
        Add(@"C:\root", "keep.txt");

        _index.RemoveSubtree(@"C:\root\sub");

        _index.Search("leaf", 10).Should().BeEmpty();
        _index.Search("deep", 10).Should().BeEmpty();
        _index.Search("keep", 10).Should().ContainSingle();
    }

    [Fact]
    public void Rename_Directory_DescendantPathsFollowInstantly()
    {
        Add(@"C:\old-name\sub", "doc.txt");

        _index.Rename(@"C:\old-name", "new-name");

        _index.Search("doc.txt", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\new-name\sub\doc.txt");
    }

    [Fact]
    public void Rename_File_UpdatesNameAndSearch()
    {
        Add(@"C:\a", "before.txt");

        _index.Rename(@"C:\a\before.txt", "after.txt");

        _index.Search("before", 10).Should().BeEmpty();
        _index.Search("after", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\a\after.txt");
    }

    [Fact]
    public void RemoveSubtree_UnknownPath_IsNoOp()
    {
        Add(@"C:\a", "x.txt");

        _index.RemoveSubtree(@"C:\nope\missing.txt");

        _index.Count.Should().BeGreaterThan(0);
    }

    private FileIndex ExportThenImport()
    {
        var restored = new FileIndex();
        var exported = new List<(int Id, int ParentId, string Name, long Size, long Ticks, bool IsDir)>();
        _index.ExportNodes((id, parent, name, size, ticks, isDir) =>
            exported.Add((id, parent, name, size, ticks, isDir)));

        foreach (var node in exported.OrderBy(n => n.Id))
        {
            restored.ImportNode(node.Id, node.ParentId, node.Name, node.Size, node.Ticks, node.IsDir);
        }

        return restored;
    }

    [Fact]
    public void ExportImport_RoundtripsLiveNodes()
    {
        Add(@"C:\data", "a.txt");
        Add(@"C:\data", "b.txt");
        _index.RemoveSubtree(@"C:\data\b.txt");

        using var restored = ExportThenImport();

        restored.Search("a.txt", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\data\a.txt");
        restored.Search("b.txt", 10).Should().BeEmpty("삭제된 노드는 내보내지 않는다");
    }

    [Fact]
    public void ExportImport_WithMiddleIdGap_PreservesParentLinks()
    {
        // 회귀: 중간 노드 삭제로 id 공백이 생겨도 parent_id 참조가 깨지면 안 된다.
        // (import가 순차 재배번하면 이 케이스에서 트리가 손상된다 — 원본 id 슬롯 복원 필수.)
        Add(@"C:\", "doomed", isDir: true);
        Add(@"C:\doomed", "victim.txt");
        Add(@"C:\survivors", "alive.txt");
        Add(@"C:\survivors\deep", "nested.txt");
        _index.RemoveSubtree(@"C:\doomed");

        using var restored = ExportThenImport();

        restored.Search("alive", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\survivors\alive.txt");
        restored.Search("nested", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"C:\survivors\deep\nested.txt");
        restored.Search("victim", 10).Should().BeEmpty();
        restored.Search("doomed", 10).Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotCorrupt()
    {
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                Add(@"C:\bulk", $"item{i}.dat");
            }
        });

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                _ = _index.Search("item", 20);
            }
        });

        await Task.WhenAll(writer, reader);

        _index.Search("item1999", 10).Should().ContainSingle();
    }
}
