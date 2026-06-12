using Explorer.Core.Sorting;
using Explorer.Core.Workspace;
using FluentAssertions;

namespace Explorer.Core.Tests.Workspace;

public sealed class PaneStateTests
{
    private static PaneState CreateWithTabs(params string[] paths)
    {
        var state = PaneState.Create(TabState.Create(paths[0]));
        foreach (var path in paths.Skip(1))
        {
            state = state.AddTab(TabState.Create(path));
        }

        return state;
    }

    [Fact]
    public void Create_HasSingleActiveTab()
    {
        var state = PaneState.Create(TabState.Create(@"C:\a"));

        state.Tabs.Should().HaveCount(1);
        state.ActiveTabIndex.Should().Be(0);
        state.ActiveTab.Path.Should().Be(@"C:\a");
    }

    [Fact]
    public void AddTab_AppendsAndActivates()
    {
        var state = CreateWithTabs(@"C:\a").AddTab(TabState.Create(@"C:\b"));

        state.Tabs.Should().HaveCount(2);
        state.ActiveTab.Path.Should().Be(@"C:\b");
    }

    [Fact]
    public void AddTab_WithoutActivate_KeepsCurrentActive()
    {
        var state = CreateWithTabs(@"C:\a").AddTab(TabState.Create(@"C:\b"), activate: false);

        state.ActiveTab.Path.Should().Be(@"C:\a");
    }

    [Fact]
    public void CloseTab_LastRemaining_IsNoOp()
    {
        var state = CreateWithTabs(@"C:\a");

        state.CloseTab(0).Should().BeSameAs(state);
    }

    [Fact]
    public void CloseTab_ActiveTab_ActivatesNeighbor()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b", @"C:\c").Activate(1);

        var closed = state.CloseTab(1);

        closed.Tabs.Select(t => t.Path).Should().Equal(@"C:\a", @"C:\c");
        closed.ActiveTab.Path.Should().Be(@"C:\c", "닫힌 자리의 다음 탭이 활성화");
    }

    [Fact]
    public void CloseTab_LastPositionActive_MovesActiveBack()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b", @"C:\c"); // active = c(2)

        var closed = state.CloseTab(2);

        closed.ActiveTab.Path.Should().Be(@"C:\b");
    }

    [Fact]
    public void CloseTab_BeforeActive_ShiftsActiveIndex()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b", @"C:\c"); // active c(2)

        var closed = state.CloseTab(0);

        closed.ActiveTab.Path.Should().Be(@"C:\c");
        closed.ActiveTabIndex.Should().Be(1);
    }

    [Fact]
    public void Activate_OutOfRange_IsNoOp()
    {
        var state = CreateWithTabs(@"C:\a");

        state.Activate(5).Should().BeSameAs(state);
        state.Activate(-1).Should().BeSameAs(state);
    }

    [Fact]
    public void UpdateActiveTab_ReplacesOnlyActiveTab()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b");

        var updated = state.UpdateActiveTab(t => t with { Path = @"C:\b\sub" });

        updated.Tabs[0].Path.Should().Be(@"C:\a");
        updated.ActiveTab.Path.Should().Be(@"C:\b\sub");
    }

    [Fact]
    public void UpdateActiveTab_PreservesSort()
    {
        var state = CreateWithTabs(@"C:\a")
            .UpdateActiveTab(t => t with { Sort = new SortDescriptor(SortColumn.Size, Descending: true) });

        state.ActiveTab.Sort.Column.Should().Be(SortColumn.Size);
    }

    [Theory]
    [InlineData(0, 2, new[] { "b", "c", "a" })]
    [InlineData(2, 0, new[] { "c", "a", "b" })]
    [InlineData(1, 1, new[] { "a", "b", "c" })]
    public void MoveTab_ReordersTabs(int from, int to, string[] expectedOrder)
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b", @"C:\c");

        var moved = state.MoveTab(from, to);

        moved.Tabs.Select(t => System.IO.Path.GetFileName(t.Path)).Should().Equal(expectedOrder);
    }

    [Fact]
    public void MoveTab_ActiveFollowsItsTab()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b", @"C:\c").Activate(0);

        var moved = state.MoveTab(0, 2);

        moved.ActiveTab.Path.Should().Be(@"C:\a");
        moved.ActiveTabIndex.Should().Be(2);
    }

    [Fact]
    public void InsertTab_BeforeActive_ShiftsActiveWhenNotActivating()
    {
        var state = CreateWithTabs(@"C:\a", @"C:\b"); // active b(1)

        var inserted = state.InsertTab(0, TabState.Create(@"C:\new"), activate: false);

        inserted.Tabs.Select(t => t.Path).Should().Equal(@"C:\new", @"C:\a", @"C:\b");
        inserted.ActiveTab.Path.Should().Be(@"C:\b");
    }

    [Fact]
    public void Operations_DoNotMutateOriginal()
    {
        var original = CreateWithTabs(@"C:\a", @"C:\b");

        _ = original.AddTab(TabState.Create(@"C:\c"));
        _ = original.CloseTab(0);
        _ = original.MoveTab(0, 1);

        original.Tabs.Should().HaveCount(2);
        original.ActiveTab.Path.Should().Be(@"C:\b");
    }

    [Fact]
    public void TabSession_RoundtripsState()
    {
        var state = TabState.Create(@"C:\data") with
        {
            Sort = new SortDescriptor(SortColumn.DateModified, Descending: true),
        };

        var roundtripped = TabSession.FromState(state).ToState();

        roundtripped.Should().Be(state);
    }
}
