using Explorer.App.Services;
using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Settings;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly IFileSystemEnumerator _enumerator = Substitute.For<IFileSystemEnumerator>();
    private readonly IDriveProvider _driveProvider = Substitute.For<IDriveProvider>();

    private MainWindowViewModel CreateViewModel(AppTheme initialTheme = AppTheme.System)
    {
        _settings.Current.Returns(new AppSettings { Theme = initialTheme });
        _settings.Update(Arg.Any<Func<AppSettings, AppSettings>>())
            .Returns(call => call.Arg<Func<AppSettings, AppSettings>>()(_settings.Current));
        _enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>([]));

        var fileList = new FileListViewModel(
            _enumerator,
            Substitute.For<IFileLauncher>(),
            _settings,
            Substitute.For<IShellIconProvider>(),
            Substitute.For<IFileOperationService>(),
            Substitute.For<IFileClipboardService>(),
            Substitute.For<IFolderWatcher>(),
            NullLogger<FileListViewModel>.Instance);
        var sidebar = new DriveSidebarViewModel(_driveProvider);
        return new MainWindowViewModel(_settings, _themeService, fileList, sidebar);
    }

    [Fact]
    public void Ctor_TakesInitialThemeFromSettings()
    {
        CreateViewModel(AppTheme.Dark).CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData(AppTheme.System, AppTheme.Light)]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, AppTheme.System)]
    public void ToggleTheme_CyclesTheme_AppliesAndPersists(AppTheme from, AppTheme expected)
    {
        var vm = CreateViewModel(from);

        vm.ToggleThemeCommand.Execute(null);

        vm.CurrentTheme.Should().Be(expected);
        _themeService.Received(1).Apply(expected);
        _settings.Received(1).Update(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public async Task FileListNavigation_SyncsAddressBarText()
    {
        var vm = CreateViewModel();

        await vm.FileList.NavigateToAsync(@"C:\Users");

        vm.AddressBar.Text.Should().Be(@"C:\Users");
    }

    [Fact]
    public void AddressBarSubmit_NavigatesFileList()
    {
        var vm = CreateViewModel();
        vm.AddressBar.Text = @"C:\Target";

        // NavigateToAsync는 CurrentPath를 첫 await 이전에 동기적으로 설정한다 — 지연 없이 검증 가능.
        vm.AddressBar.SubmitCommand.Execute(null);

        vm.FileList.CurrentPath.Should().Be(@"C:\Target");
    }

    [Fact]
    public void DriveOpen_NavigatesFileList()
    {
        var vm = CreateViewModel();
        var drive = new DriveItemViewModel(new DriveEntry(@"D:\", "Data", DriveKind.Fixed, 10, 5, IsReady: true));

        vm.DriveSidebar.OpenDriveCommand.Execute(drive);

        vm.FileList.CurrentPath.Should().Be(@"D:\");
    }
}
