using System.Windows;
using Explorer.App.ViewModels;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>저장이 적용됐는지 — 호출자가 목록을 새로고침할지 판단한다.</summary>
    public bool Saved => _viewModel.Saved;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveCommand.Execute(null);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnBrowseNetworkFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "네트워크 인덱싱 폴더 선택" };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddNetworkFolderPath(dialog.FolderName);
        }
    }
}
