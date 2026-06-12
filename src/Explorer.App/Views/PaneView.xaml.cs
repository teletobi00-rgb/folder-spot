using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Explorer.App.ViewModels;

namespace Explorer.App.Views;

public sealed class CrossPaneTabDropEventArgs : EventArgs
{
    public required PaneViewModel SourcePane { get; init; }

    public required TabViewModel Tab { get; init; }

    public int InsertIndex { get; init; }
}

public partial class PaneView : UserControl
{
    private const string TabDragFormat = "Explorer.TabDrag";

    private Point _tabDragStart;
    private TabViewModel? _tabDragCandidate;

    public PaneView()
    {
        InitializeComponent();
    }

    /// <summary>다른 페인에서 끌어온 탭이 드롭됨 — MainWindow가 워크스페이스로 라우팅한다.</summary>
    public event EventHandler<CrossPaneTabDropEventArgs>? CrossPaneTabDropRequested;

    private PaneViewModel? ViewModel => DataContext as PaneViewModel;

    private sealed record TabDragPayload(PaneViewModel SourcePane, TabViewModel Tab);

    private void OnTabStripPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(this);
        _tabDragCandidate = (e.OriginalSource as DependencyObject)
            .FindAncestorOrSelf<ListBoxItem>()?.DataContext as TabViewModel;
    }

    private void OnTabStripPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragCandidate is null || e.LeftButton != MouseButtonState.Pressed || ViewModel is null)
        {
            return;
        }

        var delta = e.GetPosition(this) - _tabDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = new TabDragPayload(ViewModel, _tabDragCandidate);
        _tabDragCandidate = null;
        var data = new DataObject(TabDragFormat, payload);
        DragDrop.DoDragDrop(TabStrip, data, DragDropEffects.Move);
    }

    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TabDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { } vm
            || e.Data.GetData(TabDragFormat) is not TabDragPayload payload)
        {
            return;
        }

        var insertIndex = ComputeInsertIndex(e.GetPosition(TabStrip));

        if (ReferenceEquals(payload.SourcePane, vm))
        {
            // 같은 페인 내 재정렬 — 제거 후 삽입이므로 뒤쪽 이동 시 인덱스 보정
            var currentIndex = IndexOfTab(payload.Tab);
            var target = insertIndex > currentIndex ? insertIndex - 1 : insertIndex;
            vm.MoveTab(payload.Tab, target);
            return;
        }

        CrossPaneTabDropRequested?.Invoke(this, new CrossPaneTabDropEventArgs
        {
            SourcePane = payload.SourcePane,
            Tab = payload.Tab,
            InsertIndex = insertIndex,
        });
    }

    private int IndexOfTab(TabViewModel tab)
    {
        for (var i = 0; i < TabStrip.Items.Count; i++)
        {
            if (ReferenceEquals(TabStrip.Items[i], tab))
            {
                return i;
            }
        }

        return TabStrip.Items.Count;
    }

    /// <summary>드롭 X좌표가 각 탭의 중간점보다 왼쪽이면 그 탭 앞에 삽입.</summary>
    private int ComputeInsertIndex(Point dropPosition)
    {
        for (var i = 0; i < TabStrip.Items.Count; i++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
            {
                var left = container.TranslatePoint(default, TabStrip).X;
                if (dropPosition.X < left + (container.ActualWidth / 2))
                {
                    return i;
                }
            }
        }

        return TabStrip.Items.Count;
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestorOrSelf<T>(this DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
