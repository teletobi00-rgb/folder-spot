using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;

namespace Explorer.App.ViewModels;

public sealed partial class AddressBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>형식이 유효한 절대 경로가 제출되면 발생한다(정규화된 경로 전달).</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>파일 목록의 현재 경로가 바뀌었을 때 표시 텍스트를 동기화한다.</summary>
    public void SetCurrentPath(string? path)
    {
        Text = path ?? string.Empty;
        ErrorText = null;
    }

    [RelayCommand]
    private void Submit()
    {
        var input = Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = PathUtils.Normalize(input);
        }
        catch (ArgumentException)
        {
            ErrorText = "올바른 절대 경로가 아닙니다 (예: C:\\Users)";
            return;
        }

        ErrorText = null;
        NavigationRequested?.Invoke(this, normalized);
    }
}
