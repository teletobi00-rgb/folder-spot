using System.Windows;
using Explorer.App.ViewModels;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class BatchRenameWindow : FluentWindow
{
    private readonly BatchRenameViewModel _viewModel;

    public BatchRenameWindow(BatchRenameViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>적용이 실제로 일어났는지 — 호출자가 목록을 새로고침할지 판단한다.</summary>
    public bool Applied => _viewModel.Applied;

    public void SetTargets(IReadOnlyList<FileItemViewModel> targets) => _viewModel.SetTargets(targets);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
