using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.App.Services;
using Explorer.Shell.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace Explorer.App.ViewModels;

/// <summary>
/// 상단 빠른 실행 바의 프로그램 하나. 아이콘은 첫 접근 시 지연 로드한다.
/// 저장 형태는 "이름|경로"(사용자 지정 이름) 또는 "경로"이며, 이름이 없으면 파일명에서 유도한다.
/// </summary>
public sealed partial class PinnedProgramViewModel : ObservableObject
{
    private const int IconLoadSize = 32;
    private ImageSource? _icon;
    private bool _iconRequested;

    [ObservableProperty]
    private string _displayName;

    /// <summary>인라인 이름 변경 편집 중 여부.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>이름 변경 편집 텍스트.</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    public PinnedProgramViewModel(string stored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stored);

        // "이름|경로" 인코딩: 첫 '|' 기준 분리(윈도우 경로엔 '|'가 올 수 없어 안전). 없으면 전체가 명령.
        var bar = stored.IndexOf('|');
        if (bar > 0 && bar < stored.Length - 1)
        {
            CustomName = stored[..bar];
            Command = stored[(bar + 1)..];
        }
        else
        {
            Command = stored;
        }

        ResolvedPath = ProgramResolver.ResolvePath(Command);
        _displayName = !string.IsNullOrWhiteSpace(CustomName) ? CustomName! : ProgramResolver.DisplayNameFor(Command);
    }

    /// <summary>실행/아이콘 대상 명령(이름 또는 전체 경로·URI).</summary>
    public string Command { get; }

    /// <summary>사용자 지정 표시 이름(없으면 null — 파일명에서 유도).</summary>
    public string? CustomName { get; private set; }

    /// <summary>아이콘 로드용 해석된 경로(못 찾으면 null).</summary>
    public string? ResolvedPath { get; }

    /// <summary>설정에 저장될 인코딩 형태("이름|경로" 또는 "경로").</summary>
    public string ToStored() => string.IsNullOrWhiteSpace(CustomName) ? Command : CustomName + "|" + Command;

    /// <summary>사용자 지정 이름을 적용한다(비우면 기본 이름으로 복귀). '|'는 구분자라 공백으로 치환.</summary>
    public void ApplyRename(string? newName)
    {
        var trimmed = newName?.Replace('|', ' ').Trim();
        CustomName = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        DisplayName = CustomName ?? ProgramResolver.DisplayNameFor(Command);
    }

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
