using Explorer.App.Tests.TestSupport;
using Explorer.App.ViewModels;
using Explorer.Core.FileSystem;
using Explorer.Core.Workspace;
using Explorer.Shell.Icons;
using FluentAssertions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class WorkspaceViewModelTests
{
    private readonly FileListTestContext _context = new();

    private WorkspaceViewModel CreateWorkspace() => new(_context.CreateFileList);

    [Fact]
    public void Ctor_StartsSinglePaneLeftActive()
    {
        var workspace = CreateWorkspace();

        workspace.IsDualMode.Should().BeFalse();
        workspace.ActiveSide.Should().Be(PaneSide.Left);
        workspace.ActivePane.Should().BeSameAs(workspace.LeftPane);
        workspace.ActiveFileList.Should().BeSameAs(workspace.LeftPane.FileList);
    }

    [Fact]
    public async Task ToggleDualMode_On_LoadsRightPane()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);
        workspace.RightPane.FileList.CurrentPath.Should().BeNull("단일 모드에서는 우측 페인이 지연 로드");

        await workspace.ToggleDualModeCommand.ExecuteAsync(null);

        workspace.IsDualMode.Should().BeTrue();
        workspace.RightPane.FileList.CurrentPath.Should().NotBeNull();
    }

    [Fact]
    public async Task ToggleDualMode_Off_ResetsActiveToLeft()
    {
        var workspace = CreateWorkspace();
        await workspace.ToggleDualModeCommand.ExecuteAsync(null);
        workspace.SetActiveSide(PaneSide.Right);
        workspace.ActiveSide.Should().Be(PaneSide.Right);

        await workspace.ToggleDualModeCommand.ExecuteAsync(null);

        workspace.IsDualMode.Should().BeFalse();
        workspace.ActiveSide.Should().Be(PaneSide.Left);
    }

    [Fact]
    public void SetActiveSide_Right_IgnoredInSingleMode()
    {
        var workspace = CreateWorkspace();

        workspace.SetActiveSide(PaneSide.Right);

        workspace.ActiveSide.Should().Be(PaneSide.Left);
    }

    [Fact]
    public async Task MoveTabToOtherPane_TransfersAndActivates()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);
        await workspace.ToggleDualModeCommand.ExecuteAsync(null);
        await workspace.LeftPane.AddTabCommand.ExecuteAsync(null);
        await workspace.LeftPane.FileList.NavigateToAsync(@"C:\moved");

        await workspace.MoveTabToOtherPaneAsync(workspace.LeftPane, workspace.LeftPane.ActiveTab!);

        workspace.LeftPane.Tabs.Should().HaveCount(1);
        workspace.RightPane.Tabs.Should().HaveCount(2);
        workspace.RightPane.ActiveTab!.Path.Should().Be(@"C:\moved");
        workspace.RightPane.FileList.CurrentPath.Should().Be(@"C:\moved");
    }

    [Fact]
    public async Task MoveTabToOtherPane_LastTab_IsNoOp()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);
        await workspace.ToggleDualModeCommand.ExecuteAsync(null);

        await workspace.MoveTabToOtherPaneAsync(workspace.LeftPane, workspace.LeftPane.ActiveTab!);

        workspace.LeftPane.Tabs.Should().HaveCount(1);
        workspace.RightPane.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Session_CaptureRestore_RoundtripsLayout()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);
        await workspace.ToggleDualModeCommand.ExecuteAsync(null);
        await workspace.LeftPane.FileList.NavigateToAsync(@"C:\left");
        await workspace.LeftPane.AddTabCommand.ExecuteAsync(null);
        await workspace.LeftPane.FileList.NavigateToAsync(@"C:\left2");
        workspace.SetActiveSide(PaneSide.Right);
        await workspace.RightPane.FileList.NavigateToAsync(@"C:\right");

        var session = workspace.CaptureSession();

        var restored = CreateWorkspace();
        await restored.RestoreSessionAsync(session);

        restored.IsDualMode.Should().BeTrue();
        restored.ActiveSide.Should().Be(PaneSide.Right);
        restored.LeftPane.Tabs.Select(t => t.Path).Should().Equal(@"C:\left", @"C:\left2");
        restored.RightPane.Tabs.Select(t => t.Path).Should().Equal(@"C:\right");
        restored.LeftPane.FileList.CurrentPath.Should().Be(@"C:\left2");
        restored.RightPane.FileList.CurrentPath.Should().Be(@"C:\right");
    }

    [Fact]
    public async Task RestoreSession_SingleMode_DoesNotLoadRightPane()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);

        workspace.LeftPane.FileList.CurrentPath.Should().NotBeNull();
        workspace.RightPane.FileList.CurrentPath.Should().BeNull();
    }

    private static FileItemViewModel Item(string path, bool isDirectory = false) => new(
        FileEntry.Create(
            path, System.IO.Path.GetFileName(path), isDirectory, 1,
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 1),
            isDirectory ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal),
        Substitute.For<IShellIconProvider>());

    private async Task<WorkspaceViewModel> CreateDualWorkspaceAsync(string leftPath = @"C:\src", string rightPath = @"C:\dst")
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreSessionAsync(null);
        await workspace.ToggleDualModeCommand.ExecuteAsync(null);
        await workspace.LeftPane.FileList.NavigateToAsync(leftPath);
        await workspace.RightPane.FileList.NavigateToAsync(rightPath);
        return workspace;
    }

    [Fact]
    public async Task SwitchPane_TogglesActiveSide_OnlyInDualMode()
    {
        var workspace = CreateWorkspace();
        workspace.SwitchPaneCommand.CanExecute(null).Should().BeFalse("단일 모드에서는 비활성");

        await workspace.ToggleDualModeCommand.ExecuteAsync(null);
        workspace.SwitchPaneCommand.CanExecute(null).Should().BeTrue();

        workspace.SwitchPaneCommand.Execute(null);
        workspace.ActiveSide.Should().Be(PaneSide.Right);

        workspace.SwitchPaneCommand.Execute(null);
        workspace.ActiveSide.Should().Be(PaneSide.Left);
    }

    [Fact]
    public async Task SwapPanes_ExchangesPaneContents_CursorStaysOnSameSide()
    {
        var workspace = await CreateDualWorkspaceAsync(@"C:\left", @"C:\right");

        workspace.SwapPanesCommand.Execute(null);

        workspace.LeftPane.FileList.CurrentPath.Should().Be(@"C:\right");
        workspace.RightPane.FileList.CurrentPath.Should().Be(@"C:\left");
        workspace.ActiveSide.Should().Be(PaneSide.Left, "TC 동작: 커서는 같은 쪽에 남는다");
        workspace.ActiveFileList.CurrentPath.Should().Be(@"C:\right");
    }

    [Fact]
    public async Task CopyToOtherPane_CopiesSelectionToInactivePaneFolder()
    {
        var workspace = await CreateDualWorkspaceAsync();
        workspace.LeftPane.FileList.SelectedItems = [Item(@"C:\src\a.txt"), Item(@"C:\src\b.txt")];
        string[] expected = [@"C:\src\a.txt", @"C:\src\b.txt"];

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        await _context.Operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(expected)),
            @"C:\dst");
    }

    [Fact]
    public async Task MoveToOtherPane_MovesSelectionToInactivePaneFolder()
    {
        var workspace = await CreateDualWorkspaceAsync();
        workspace.LeftPane.FileList.SelectedItems = [Item(@"C:\src\a.txt")];
        string[] expected = [@"C:\src\a.txt"];

        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        await _context.Operations.Received(1).MoveAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(expected)),
            @"C:\dst");
    }

    [Fact]
    public async Task TransferToOtherPane_SameFolder_IsNoOpWithMessage()
    {
        var workspace = await CreateDualWorkspaceAsync(@"C:\same", @"C:\same");
        workspace.LeftPane.FileList.SelectedItems = [Item(@"C:\same\a.txt")];

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        await _context.Operations.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!);
        workspace.LeftPane.FileList.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TransferToOtherPane_EmptySelection_IsNoOp()
    {
        var workspace = await CreateDualWorkspaceAsync();

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        await _context.Operations.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!);
    }

    [Fact]
    public async Task MoveToOtherPane_FolderIntoItsDescendant_IsBlocked()
    {
        var workspace = await CreateDualWorkspaceAsync(@"C:\src", @"C:\src\folder\inner");
        workspace.LeftPane.FileList.SelectedItems = [Item(@"C:\src\folder", isDirectory: true)];

        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        await _context.Operations.DidNotReceiveWithAnyArgs().MoveAsync(default!, default!);
        workspace.LeftPane.FileList.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OpenInOtherPane_SelectedFolder_OpensItInInactivePane()
    {
        var workspace = await CreateDualWorkspaceAsync();
        workspace.LeftPane.FileList.SelectedItem = Item(@"C:\src\docs", isDirectory: true);

        await workspace.OpenInOtherPaneCommand.ExecuteAsync(null);

        workspace.RightPane.FileList.CurrentPath.Should().Be(@"C:\src\docs");
    }

    [Fact]
    public async Task OpenInOtherPane_FileSelected_MirrorsCurrentFolder()
    {
        var workspace = await CreateDualWorkspaceAsync();
        workspace.LeftPane.FileList.SelectedItem = Item(@"C:\src\a.txt");

        await workspace.OpenInOtherPaneCommand.ExecuteAsync(null);

        workspace.RightPane.FileList.CurrentPath.Should().Be(@"C:\src");
    }
}
