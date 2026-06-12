using Explorer.App.ViewModels;
using Explorer.Core.FileSystem;
using FluentAssertions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class DriveSidebarViewModelTests
{
    private readonly IDriveProvider _provider = Substitute.For<IDriveProvider>();

    [Fact]
    public void Refresh_MapsDrivesToItems()
    {
        _provider.GetDrives().Returns(
        [
            new DriveEntry(@"C:\", "Windows", DriveKind.Fixed, 1000, 400, IsReady: true),
            new DriveEntry(@"D:\", "", DriveKind.Optical, 0, 0, IsReady: false),
        ]);
        var vm = new DriveSidebarViewModel(_provider);

        vm.RefreshCommand.Execute(null);

        vm.Drives.Should().HaveCount(2);
        vm.Drives[0].DisplayName.Should().Be("Windows (C:)");
        vm.Drives[1].DisplayName.Should().Be("DVD 드라이브 (D:)");
        vm.Drives[1].DetailText.Should().Be("준비되지 않음");
    }

    [Fact]
    public void OpenDrive_RaisesEventWithRootPath()
    {
        var vm = new DriveSidebarViewModel(_provider);
        string? opened = null;
        vm.DriveOpenRequested += (_, path) => opened = path;

        vm.OpenDriveCommand.Execute(new DriveItemViewModel(
            new DriveEntry(@"C:\", "OS", DriveKind.Fixed, 100, 50, IsReady: true)));

        opened.Should().Be(@"C:\");
    }

    [Fact]
    public void OpenDrive_Null_DoesNotRaise()
    {
        var vm = new DriveSidebarViewModel(_provider);
        var raised = false;
        vm.DriveOpenRequested += (_, _) => raised = true;

        vm.OpenDriveCommand.Execute(null);

        raised.Should().BeFalse();
    }
}
