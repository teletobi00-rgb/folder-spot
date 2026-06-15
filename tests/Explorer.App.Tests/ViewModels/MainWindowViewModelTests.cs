using Explorer.App.Services;
using Explorer.App.Tests.TestSupport;
using Explorer.App.ViewModels;
using Explorer.Core.FileSystem;
using Explorer.Core.Input;
using Explorer.Core.Settings;
using Explorer.Core.Workspace;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly FileListTestContext _context = new();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly IDriveProvider _driveProvider = Substitute.For<IDriveProvider>();

    private MainWindowViewModel CreateViewModel(AppTheme initialTheme = AppTheme.System)
    {
        _context.Settings.Current.Returns(new AppSettings { Theme = initialTheme });
        _context.Settings.Update(Arg.Any<Func<AppSettings, AppSettings>>())
            .Returns(call => call.Arg<Func<AppSettings, AppSettings>>()(_context.Settings.Current));

        var workspace = new WorkspaceViewModel(
            _context.CreateFileList, _context.Undo, _context.PreviewRegistry, TimeSpan.FromMilliseconds(10));
        var sidebar = new DriveSidebarViewModel(_driveProvider);
        var favorites = new FavoritesViewModel(
            _context.Favorites, Substitute.For<IFileLauncher>(), NullLogger<FavoritesViewModel>.Instance);
        var operationQueue = new OperationQueueViewModel(_context.Queue);
        return new MainWindowViewModel(
            _context.Settings, _themeService, workspace, sidebar, favorites, operationQueue, KeyMap.CreateDefault());
    }

    [Fact]
    public void Ctor_TakesInitialThemeFromSettings()
    {
        CreateViewModel(AppTheme.Dark).CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData(AppTheme.System, AppTheme.Dark)]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, AppTheme.Light)]
    public void ToggleTheme_TogglesDarkLight_AppliesAndPersists(AppTheme from, AppTheme expected)
    {
        var vm = CreateViewModel(from);

        vm.ToggleThemeCommand.Execute(null);

        vm.CurrentTheme.Should().Be(expected);
        _themeService.Received(1).Apply(expected);
        _context.Settings.Received(1).Update(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public async Task ActivePaneNavigation_SyncsAddressBar()
    {
        var vm = CreateViewModel();

        await vm.Workspace.ActiveFileList.NavigateToAsync(@"C:\Users");

        vm.AddressBar.Text.Should().Be(@"C:\Users");
    }

    [Fact]
    public async Task SwitchingActivePane_RewiresAddressBarToThatPane()
    {
        var vm = CreateViewModel();
        await vm.Workspace.ToggleDualModeCommand.ExecuteAsync(null);
        await vm.Workspace.LeftPane.FileList.NavigateToAsync(@"C:\left");
        await vm.Workspace.RightPane.FileList.NavigateToAsync(@"C:\right");

        vm.Workspace.SetActiveSide(PaneSide.Right);
        vm.AddressBar.Text.Should().Be(@"C:\right");

        // 이제 우측 페인 탐색만 주소창에 반영되어야 한다
        await vm.Workspace.RightPane.FileList.NavigateToAsync(@"C:\right2");
        vm.AddressBar.Text.Should().Be(@"C:\right2");

        await vm.Workspace.LeftPane.FileList.NavigateToAsync(@"C:\left2");
        vm.AddressBar.Text.Should().Be(@"C:\right2", "비활성 페인 탐색은 주소창에 반영되지 않는다");
    }

    [Fact]
    public void AddressBarSubmit_NavigatesActivePane()
    {
        var vm = CreateViewModel();
        vm.AddressBar.Text = @"C:\Target";

        vm.AddressBar.SubmitCommand.Execute(null);

        vm.Workspace.ActiveFileList.CurrentPath.Should().Be(@"C:\Target");
    }

    [Fact]
    public void DriveOpen_NavigatesActivePane()
    {
        var vm = CreateViewModel();
        var drive = new DriveItemViewModel(new DriveEntry(@"D:\", "Data", DriveKind.Fixed, 10, 5, IsReady: true));

        vm.DriveSidebar.OpenDriveCommand.Execute(drive);

        vm.Workspace.ActiveFileList.CurrentPath.Should().Be(@"D:\");
    }

    [Fact]
    public async Task SaveSession_PersistsWorkspaceIntoSettings()
    {
        var vm = CreateViewModel();
        await vm.Workspace.ActiveFileList.NavigateToAsync(@"C:\saved");
        AppSettings? saved = null;
        _context.Settings.Update(Arg.Any<Func<AppSettings, AppSettings>>())
            .Returns(call =>
            {
                saved = call.Arg<Func<AppSettings, AppSettings>>()(_context.Settings.Current);
                return saved;
            });

        vm.SaveSession();

        saved.Should().NotBeNull();
        saved!.Session.Should().NotBeNull();
        saved.Session!.Left.Tabs.Single().Path.Should().Be(@"C:\saved");
    }
}
