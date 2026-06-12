using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Formatting;

namespace Explorer.App.ViewModels;

public sealed partial class DriveSidebarViewModel : ObservableObject
{
    private readonly IDriveProvider _driveProvider;

    [ObservableProperty]
    private IReadOnlyList<DriveItemViewModel> _drives = [];

    public DriveSidebarViewModel(IDriveProvider driveProvider)
    {
        ArgumentNullException.ThrowIfNull(driveProvider);
        _driveProvider = driveProvider;
    }

    /// <summary>드라이브 항목 클릭 시 루트 경로와 함께 발생한다.</summary>
    public event EventHandler<string>? DriveOpenRequested;

    [RelayCommand]
    private void Refresh()
    {
        Drives = _driveProvider.GetDrives().Select(d => new DriveItemViewModel(d)).ToArray();
    }

    [RelayCommand]
    private void OpenDrive(DriveItemViewModel? drive)
    {
        if (drive is not null)
        {
            DriveOpenRequested?.Invoke(this, drive.RootPath);
        }
    }
}

public sealed class DriveItemViewModel
{
    public DriveItemViewModel(DriveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RootPath = entry.RootPath;

        var letter = entry.RootPath.TrimEnd('\\', '/');
        var label = string.IsNullOrEmpty(entry.Label) ? DefaultLabel(entry.Kind) : entry.Label;
        DisplayName = $"{label} ({letter})";

        DetailText = entry.IsReady
            ? $"{FileSizeFormatter.Format(entry.FreeSpace)} 사용 가능 / {FileSizeFormatter.Format(entry.TotalSize)}"
            : "준비되지 않음";
    }

    public string RootPath { get; }

    public string DisplayName { get; }

    public string DetailText { get; }

    private static string DefaultLabel(DriveKind kind) => kind switch
    {
        DriveKind.Fixed => "로컬 디스크",
        DriveKind.Removable => "이동식 디스크",
        DriveKind.Network => "네트워크 드라이브",
        DriveKind.Optical => "DVD 드라이브",
        DriveKind.Ram => "램 디스크",
        _ => "드라이브",
    };
}
