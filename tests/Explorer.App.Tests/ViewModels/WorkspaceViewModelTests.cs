using Explorer.App.Tests.TestSupport;
using Explorer.App.ViewModels;
using Explorer.Core.Workspace;
using FluentAssertions;

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
}
