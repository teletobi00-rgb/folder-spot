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
    public async Task SwitchTab_IsolatesBackHistoryPerTab()
    {
        var pane = CreatePane(@"C:\a");
        await pane.ActivateCurrentTabAsync();

        // 첫 탭에서 a → b → c 이동 (뒤로가기 가능 상태)
        await pane.FileList.NavigateToAsync(@"C:\b");
        await pane.FileList.NavigateToAsync(@"C:\c");
        pane.FileList.History.CanGoBack.Should().BeTrue();

        // 새 탭(현재 경로 c 복제) — 자체 히스토리는 비어 있어 뒤로가기 불가여야 한다.
        await pane.AddTabCommand.ExecuteAsync(null);
        pane.FileList.CurrentPath.Should().Be(@"C:\c");
        pane.FileList.History.CanGoBack.Should().BeFalse("새 탭은 다른 탭의 이동 내역을 물려받지 않아야 한다");

        // 새 탭에서 c → d 이동.
        await pane.FileList.NavigateToAsync(@"C:\d");

        // 첫 탭으로 전환 — 첫 탭 히스토리(a,b,c)가 복원되어 뒤로가면 b여야 한다.
        pane.ActiveTab = pane.Tabs[0];
        await FileListTestContext.WaitUntilAsync(() => pane.FileList.CurrentPath == @"C:\c");
        pane.FileList.History.CanGoBack.Should().BeTrue();
        await pane.FileList.GoBackCommand.ExecuteAsync(null);
        pane.FileList.CurrentPath.Should().Be(@"C:\b", "첫 탭의 뒤로가기는 첫 탭 내역만 따라야 한다");

        // 둘째 탭으로 전환 — 둘째 탭 히스토리(c,d)가 복원되어 뒤로가면 c여야 한다.
        pane.ActiveTab = pane.Tabs[1];
        await FileListTestContext.WaitUntilAsync(() => pane.FileList.CurrentPath == @"C:\d");
        pane.FileList.History.CanGoBack.Should().BeTrue();
        await pane.FileList.GoBackCommand.ExecuteAsync(null);
        pane.FileList.CurrentPath.Should().Be(@"C:\c", "둘째 탭의 뒤로가기는 둘째 탭 내역만 따라야 한다");
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
