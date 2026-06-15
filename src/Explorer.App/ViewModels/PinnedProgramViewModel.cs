using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.App.Services;
using Explorer.Shell.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace Explorer.App.ViewModels;

/// <summary>상단 빠른 실행 바의 프로그램 하나. 아이콘은 첫 접근 시 지연 로드한다.</summary>
public sealed class PinnedProgramViewModel : ObservableObject
{
    private const int IconLoadSize = 32;
    private ImageSource? _icon;
    private bool _iconRequested;

    public PinnedProgramViewModel(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        Command = command;
        ResolvedPath = ProgramResolver.ResolvePath(command);
        DisplayName = ProgramResolver.DisplayNameFor(command);
    }

    /// <summary>저장되는 원본 명령(이름 또는 전체 경로).</summary>
    public string Command { get; }

    /// <summary>아이콘 로드용 해석된 경로(못 찾으면 null).</summary>
    public string? ResolvedPath { get; }

    public string DisplayName { get; }

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }

            return _icon;
        }
    }

    private async Task LoadIconAsync()
    {
        if (ResolvedPath is null)
        {
            return;
        }

        var provider = App.Services.GetService<IShellThumbnailProvider>();
        if (provider is null)
        {
            return;
        }

        var image = await provider.GetThumbnailAsync(ResolvedPath, IconLoadSize).ConfigureAwait(true);
        if (image is not null)
        {
            _icon = image;
            OnPropertyChanged(nameof(Icon));
        }
    }
}
