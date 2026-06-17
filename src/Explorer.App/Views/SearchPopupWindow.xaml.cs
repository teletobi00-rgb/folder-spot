using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Explorer.App.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class SearchPopupWindow : FluentWindow
{
    private readonly SearchPopupViewModel _viewModel;

    private bool _contextMenuOpen;

    public SearchPopupWindow(SearchPopupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _viewModel.HideRequested += (_, _) => Hide();

        // 컨텍스트 메뉴가 열리면 창이 비활성화되며 OnDeactivated가 팝업을 숨길 수 있으므로, 열림 동안 숨김을 막는다.
        AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler((_, _) => _contextMenuOpen = true), handledEventsToo: true);
        AddHandler(ContextMenuClosingEvent, new ContextMenuEventHandler((_, _) => _contextMenuOpen = false), handledEventsToo: true);
    }

    /// <summary>전역 핫키 진입점 — 보이면 숨기고, 숨겨져 있으면 화면 중앙 상단에 띄운다.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + (workArea.Height * 0.22);

        _viewModel.OnShown();
        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();

        // static Log는 SourceContext가 없어 Debug가 필터링된다 — 저빈도 이벤트라 Information 사용.
        Log.Information("검색 팝업 표시");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Hide();
                break;
            case Key.Down:
                e.Handled = true;
                _viewModel.MoveSelection(+1);
                ScrollSelectionIntoView();
                break;
            case Key.Up:
                e.Handled = true;
                _viewModel.MoveSelection(-1);
                ScrollSelectionIntoView();
                break;
            case Key.Return when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                e.Handled = true;
                _viewModel.RevealSelectedCommand.Execute(null);
                break;
            case Key.Return:
                e.Handled = true;
                _viewModel.OpenSelectedCommand.Execute(null);
                break;
        }
    }

    private void ScrollSelectionIntoView()
    {
        if (_viewModel.Selected is { } selected)
        {
            ResultsList.ScrollIntoView(selected);
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OpenSelectedCommand.Execute(null);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // 컨텍스트 메뉴가 열려 있으면 숨기지 않는다(메뉴 항목을 고를 기회 보장).
        if (_contextMenuOpen)
        {
            return;
        }

        // 포커스를 잃으면 런처처럼 사라진다 (종료 아님 — 핫키로 즉시 재호출 가능)
        Hide();
    }

    private void OnAddToQuickLaunchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchResultItemViewModel item })
        {
            _viewModel.AddToQuickLaunchCommand.Execute(item);
        }
    }
}
