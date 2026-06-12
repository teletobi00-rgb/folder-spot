using Explorer.App.Tests.TestSupport;
using Explorer.App.ViewModels;
using Explorer.Core.Sorting;
using FluentAssertions;

namespace Explorer.App.Tests.ViewModels;

public sealed class PaneViewModelTests
{
    private readonly FileListTestContext _context = new();

    private PaneViewModel CreatePane(string initialPath = @"C:\start") =>
        new(_context.CreateFileList(), initialPath);

    [Fact]
    public void Ctor_HasSingleTabWithInitialPath()
    {
        var pane = CreatePane(@"C:\start");

        pane.Tabs.Should().HaveCount(1);
        pane.ActiveTab!.Path.Should().Be(@"C:\start");
        pane.ActiveTab.Title.Should().Be("start");
    }

    [Fact]
    public async Task ActivateCurrentTab_NavigatesFileList()
    {
        var pane = CreatePane(@"C:\start");

        await pane.ActivateCurrentTabAsync();

        pane.FileList.CurrentPath.Should().Be(@"C:\start");
    }

    [Fact]
    public async Task AddTab_DuplicatesCurrentPath_AndActivates()
    {
        var pane = CreatePane(@"C:\start");
        await pane.ActivateCurrentTabAsync();

        await pane.AddTabCommand.ExecuteAsync(null);

        pane.Tabs.Should().HaveCount(2);
        pane.ActiveTab.Should().BeSameAs(pane.Tabs[1]);
        pane.FileList.CurrentPath.Should().Be(@"C:\start");
    }

    [Fact]
    public async Task Navigation_UpdatesActiveTabPathAndTitle()
    {
        var pane = CreatePane(@"C:\start");
        await pane.ActivateCurrentTabAsync();

        await pane.FileList.NavigateToAsync(@"C:\start\문서");

        pane.ActiveTab!.Path.Should().Be(@"C:\start\문서");
        pane.ActiveTab.Title.Should().Be("문서");
    }

    [Fact]
    public async Task SwitchTab_RestoresPathAndSortOfThatTab()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();
        pane.FileList.Sort = new SortDescriptor(SortColumn.Size, Descending: true);

        await pane.AddTabCommand.ExecuteAsync(null);
        await pane.FileList.NavigateToAsync(@"C:\b");
        pane.FileList.Sort = new SortDescriptor(SortColumn.Name, Descending: false);

        // 첫 탭으로 전환
        pane.ActiveTab = pane.Tabs[0];
        await FileListTestContext.WaitUntilAsync(() => pane.FileList.CurrentPath == @"C:\a");

        pane.FileList.Sort.Should().Be(new SortDescriptor(SortColumn.Size, Descending: true));

        // 둘째 탭으로 복귀 — 경로/정렬이 보존되어 있어야 함
        pane.ActiveTab = pane.Tabs[1];
        await FileListTestContext.WaitUntilAsync(() => pane.FileList.CurrentPath == @"C:\b");
        pane.FileList.Sort.Should().Be(new SortDescriptor(SortColumn.Name, Descending: false));
    }

    [Fact]
    public async Task CloseActiveTab_ActivatesNeighborAndNavigates()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();
        await pane.AddTabCommand.ExecuteAsync(null);
        await pane.FileList.NavigateToAsync(@"C:\b");

        await pane.CloseTabCommand.ExecuteAsync(pane.ActiveTab);

        pane.Tabs.Should().HaveCount(1);
        pane.ActiveTab!.Path.Should().Be(@"C:\a");
        pane.FileList.CurrentPath.Should().Be(@"C:\a");
    }

    [Fact]
    public async Task CloseLastTab_IsNoOp()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();

        await pane.CloseTabCommand.ExecuteAsync(pane.ActiveTab);

        pane.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public async Task MoveTab_ReordersStrip()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();
        await pane.FileList.NavigateToAsync(@"C:\a");
        await pane.AddTabCommand.ExecuteAsync(null);
        await pane.FileList.NavigateToAsync(@"C:\b");

        pane.MoveTab(pane.Tabs[1], 0);

        pane.Tabs.Select(t => t.Path).Should().Equal(@"C:\b", @"C:\a");
        pane.ActiveTab!.Path.Should().Be(@"C:\b");
    }

    [Fact]
    public async Task Session_CaptureAndRestore_Roundtrips()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();
        pane.FileList.Sort = new SortDescriptor(SortColumn.DateModified, Descending: true);
        await pane.AddTabCommand.ExecuteAsync(null);
        await pane.FileList.NavigateToAsync(@"C:\b");

        var session = pane.CaptureSession();

        var restored = CreatePane(@"C:\ignored");
        restored.RestoreSession(session);

        restored.Tabs.Select(t => t.Path).Should().Equal(@"C:\a", @"C:\b");
        restored.ActiveTab!.Path.Should().Be(@"C:\b");
        await restored.ActivateCurrentTabAsync();
        restored.FileList.CurrentPath.Should().Be(@"C:\b");
    }
}
