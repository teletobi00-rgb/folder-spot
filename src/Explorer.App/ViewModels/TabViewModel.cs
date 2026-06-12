using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Explorer.App.ViewModels;

/// <summary>탭 스트립의 탭 하나 (표시 전용 — 진실의 원천은 PaneViewModel의 PaneState).</summary>
public sealed partial class TabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    public TabViewModel(string path)
    {
        UpdatePath(path);
    }

    public void UpdatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Title = ToTitle(path);
    }

    /// <summary>폴더명 (루트는 "C:\" 그대로).</summary>
    private static string ToTitle(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
