using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Explorer.App.ViewModels;
using Explorer.Core.FileSystem;
using Explorer.Core.Sorting;
using Explorer.Shell.ContextMenu;
using Microsoft.Extensions.DependencyInjection;

namespace Explorer.App.Views;

public partial class FileListView : UserControl
{
    private Point _dragStartPoint;
    private bool _dragStartedOnItem;
    private bool _suppressBackgroundMenuOnce;
    private Point _lastItemMenuScreenPoint;
    private ListViewItem? _dropHighlightedItem;
    private bool _marqueePending;
    private bool _marqueeActive;
    private ListView? _marqueeList;
    private Point _marqueeOrigin;

    /// <summary>일괄 이름 변경 단축키용 RoutedCommand — 다이얼로그가 뷰를 필요로 해 코드비하인드에서 처리한다.</summary>
    public static readonly RoutedUICommand BatchRenameCommand = new("일괄 이름 변경", "BatchRename", typeof(FileListView));

    /// <summary>폴더 크기 계산 단축키용 RoutedCommand.</summary>
    public static readonly RoutedUICommand FolderSizeCommand = new("폴더 크기 계산", "FolderSize", typeof(FileListView));

    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // B6 단축키: 일괄 이름 변경(Ctrl+Shift+R), 폴더 크기 계산(Alt+Shift+S).
        // UserControl 레벨 InputBinding이라 어느 뷰(Details/List/Thumbnail)에서 포커스가 있든 동작한다.
        CommandBindings.Add(new CommandBinding(BatchRenameCommand, (s, e) => OnBatchRenameClick(s, e)));
        CommandBindings.Add(new CommandBinding(FolderSizeCommand, (_, _) => ViewModel?.CalculateFolderSizeCommand.Execute(null)));
        InputBindings.Add(new KeyBinding(BatchRenameCommand, Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(FolderSizeCommand, Key.S, ModifierKeys.Alt | ModifierKeys.Shift));
    }

    private FileListViewModel? ViewModel => DataContext as FileListViewModel;

    private static IShellContextMenuService ContextMenuService =>
        App.Services.GetRequiredService<IShellContextMenuService>();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileListViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is FileListViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateStatusText(newVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VM 속성 변경은 항상 UI 스레드에서 발생한다(FileListViewModel의 스레드 계약).
        if (e.PropertyName is nameof(FileListViewModel.Status) or nameof(FileListViewModel.StatusMessage)
            && sender is FileListViewModel vm)
        {
            UpdateStatusText(vm);
        }
    }

    private void UpdateStatusText(FileListViewModel vm)
    {
        var text = vm.Status switch
        {
            FileListStatus.Empty => "폴더가 비어 있습니다",
            FileListStatus.NotFound => "경로를 찾을 수 없습니다",
            FileListStatus.AccessDenied => "이 폴더에 접근할 권한이 없습니다",
            FileListStatus.Error => vm.StatusMessage ?? "오류가 발생했습니다",
            _ => null,
        };

        StatusText.Text = text ?? string.Empty;
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Ctrl+F → 빠른 필터 포커스, Space → 빠른 미리보기. 편집 중에는 무시.</summary>
    private void OnFileListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            FilterBox.Focus();
            FilterBox.SelectAll();
            return;
        }

        if (e.Key != Key.Space || e.OriginalSource is TextBox)
        {
            return; // 이름 변경 등 편집 중에는 기본 동작
        }

        if (ViewModel?.SelectedItem is { IsDirectory: false } file)
        {
            e.Handled = true;
            App.Services.GetRequiredService<QuickPreviewWindow>().Toggle(file.Entry.FullPath);
        }
    }

    /// <summary>필터 박스: Esc로 필터 해제+목록 복귀, Down/Enter로 목록으로 이동.</summary>
    private void OnFilterBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (ViewModel is not null)
            {
                ViewModel.FilterText = string.Empty;
            }

            FileList.Focus();
        }
        else if (e.Key is Key.Down or Key.Return)
        {
            e.Handled = true;
            if (ViewModel is { Items.Count: > 0 } vm)
            {
                vm.SelectedItem ??= vm.Items[0];
            }

            FileList.Focus();
        }
    }

    private void OnBatchRenameClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var targets = ViewModel.SelectedItems;
        if (targets.Count == 0 && ViewModel.SelectedItem is { } single)
        {
            targets = [single];
        }

        if (targets.Count == 0)
        {
            return;
        }

        var window = App.Services.GetRequiredService<BatchRenameWindow>();
        window.Owner = Window.GetWindow(this);
        window.SetTargets(targets);
        window.ShowDialog();

        if (window.Applied)
        {
            ViewModel.RefreshCommand.Execute(null);
        }
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || ViewModel is null)
        {
            return;
        }

        SortColumn? sortColumn = column == NameColumn ? SortColumn.Name
            : column == ExtensionColumn ? SortColumn.Extension
            : column == SizeColumn ? SortColumn.Size
            : column == DateModifiedColumn ? SortColumn.DateModified
            : column == AttributesColumn ? SortColumn.Attributes
            : null;

        if (sortColumn is { } target)
        {
            ViewModel.ChangeSortCommand.Execute(target);
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 헤더/빈 영역 더블클릭을 거르기 위해 실제 행(ListViewItem) 위에서 발생했는지 확인한다.
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<ListViewItem>(source)?.DataContext is FileItemViewModel item
            && ViewModel is not null)
        {
            ViewModel.OpenItemCommand.Execute(item);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null && sender is ListView list)
        {
            ViewModel.SelectedItems = list.SelectedItems.Cast<FileItemViewModel>().ToArray();
        }
    }

    // ─── 드래그 시작 (앱 → 탐색기/다른 곳) ─────────────────────────────────────────

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _marqueePending = false;
        _marqueeActive = false;

        var noModifier = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0;
        var onItem = e.OriginalSource is DependencyObject source
            && e.OriginalSource is not System.Windows.Controls.TextBox
            && FindAncestor<ListViewItem>(source) is not null;

        // 항목 위 + 수식어 없음: 앱→탐색기 드래그 후보(드래그 시작으로 취급하면 선택 확장이 깨지므로 Shift/Ctrl 제외).
        _dragStartedOnItem = noModifier && onItem;

        // 빈 영역 + 수식어 없음: 드래그 영역 선택(마퀴) 후보. 임계 거리를 넘으면 시작한다.
        if (!onItem && noModifier && sender is ListView list)
        {
            _marqueeList = list;
            _marqueeOrigin = e.GetPosition(MarqueeLayer);
            _marqueePending = true;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // 마퀴(영역 선택) 시작/진행 — 빈 영역에서 임계 거리 넘게 드래그.
        if ((_marqueePending || _marqueeActive) && _marqueeList is { } list)
        {
            if (!_marqueeActive)
            {
                var d = e.GetPosition(this) - _dragStartPoint;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _marqueeActive = true;
                list.Focus();
                Mouse.Capture(list);
                MarqueeBox.Visibility = Visibility.Visible;
            }

            UpdateMarqueeBox(e.GetPosition(MarqueeLayer));
            UpdateMarqueeSelection(list);
            e.Handled = true;
            return;
        }

        // 항목 드래그(앱 → 탐색기/다른 곳).
        if (!_dragStartedOnItem || ViewModel is not { SelectedItems.Count: > 0 } vm)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartedOnItem = false;
        var paths = vm.SelectedItems.Select(i => i.Entry.FullPath).ToArray();
        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(
            sender as ListView ?? FileList, data,
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeActive)
        {
            Mouse.Capture(null);
            MarqueeBox.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        _marqueePending = false;
        _marqueeActive = false;
    }

    private void UpdateMarqueeBox(Point current)
    {
        Canvas.SetLeft(MarqueeBox, Math.Min(_marqueeOrigin.X, current.X));
        Canvas.SetTop(MarqueeBox, Math.Min(_marqueeOrigin.Y, current.Y));
        MarqueeBox.Width = Math.Abs(current.X - _marqueeOrigin.X);
        MarqueeBox.Height = Math.Abs(current.Y - _marqueeOrigin.Y);
    }

    private void UpdateMarqueeSelection(ListView list)
    {
        var rect = new Rect(
            Canvas.GetLeft(MarqueeBox), Canvas.GetTop(MarqueeBox), MarqueeBox.Width, MarqueeBox.Height);

        var selected = new List<object>();
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem { IsVisible: true } container)
            {
                var bounds = container
                    .TransformToVisual(MarqueeLayer)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (rect.IntersectsWith(bounds))
                {
                    selected.Add(list.Items[i]);
                }
            }
        }

        // 선택이 실제로 바뀐 경우에만 갱신(SelectionChanged 폭주 방지).
        if (selected.Count == list.SelectedItems.Count && selected.All(list.SelectedItems.Contains))
        {
            return;
        }

        list.SelectedItems.Clear();
        foreach (var item in selected)
        {
            list.SelectedItems.Add(item);
        }
    }

    // ─── 드롭 수신 (탐색기/다른 곳 → 앱) ─────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (ResolveDrop(e) is not { } resolved)
        {
            ClearDropHighlight();
            return;
        }

        e.Effects = resolved.Operation == DropOperation.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        UpdateDropHighlight(resolved.TargetItem);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHighlight();

        if (ResolveDrop(e) is { } resolved && ViewModel is { } vm)
        {
            _ = vm.HandleDropAsync(resolved.Paths, resolved.TargetDir, resolved.Operation);
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ClearDropHighlight();

    private (string[] Paths, string TargetDir, DropOperation Operation, ListViewItem? TargetItem)? ResolveDrop(DragEventArgs e)
    {
        if (ViewModel is not { CurrentPath: { } currentPath }
            || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return null;
        }

        // 폴더 행 위에 드롭하면 그 폴더가, 아니면 현재 폴더가 대상이다.
        ListViewItem? targetItem = null;
        var targetDir = currentPath;
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<ListViewItem>(source) is { DataContext: FileItemViewModel { IsDirectory: true } dirItem } item)
        {
            targetItem = item;
            targetDir = dirItem.Entry.FullPath;
        }

        var operation = DropRules.Resolve(
            copyModifier: (e.KeyStates & DragDropKeyStates.ControlKey) != 0,
            moveModifier: (e.KeyStates & DragDropKeyStates.ShiftKey) != 0,
            sameVolume: DropRules.IsSameVolume(paths[0], targetDir));

        return DropRules.CanDrop(paths, targetDir, operation)
            ? (paths, targetDir, operation, targetItem)
            : null;
    }

    private void UpdateDropHighlight(ListViewItem? item)
    {
        // Recycling 가상화에서 컨테이너가 재사용되므로 컨테이너+데이터 쌍으로 동일성을 판단한다.
        if (ReferenceEquals(_dropHighlightedItem, item)
            && ReferenceEquals(_dropHighlightedItem?.DataContext, item?.DataContext))
        {
            return;
        }

        ClearDropHighlight();
        if (item is not null)
        {
            _dropHighlightedItem = item;
            item.Background = SystemColors.AccentColorLight2Brush;
        }
    }

    private void ClearDropHighlight()
    {
        // 저장해둔 브러시 복원 대신 ClearValue — 재활용된 컨테이너에 낡은 브러시를 칠하는 문제를 차단한다.
        _dropHighlightedItem?.ClearValue(BackgroundProperty);
        _dropHighlightedItem = null;
    }

    // ─── 컨텍스트 메뉴 ───────────────────────────────────────────────────────────

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm
            || e.OriginalSource is not DependencyObject source
            || FindAncestor<ListViewItem>(source) is not { DataContext: FileItemViewModel item } container)
        {
            return; // 빈 영역 — WPF 배경 ContextMenu가 열린다
        }

        // 선택 밖의 항목을 우클릭하면 그 항목만 선택 (탐색기 동작)
        if (!FileList.SelectedItems.Contains(item))
        {
            FileList.SelectedItems.Clear();
            container.IsSelected = true;
        }

        e.Handled = true;
        _suppressBackgroundMenuOnce = true;
        _lastItemMenuScreenPoint = PointToScreen(e.GetPosition(this));

        // 자체 메뉴를 기본으로 연다 — 네이티브 메뉴는 메뉴 안의 옵트인 항목으로 제공.
        if (Resources["ItemContextMenu"] is ContextMenu menu)
        {
            menu.DataContext = vm;
            menu.PlacementTarget = FileList;
            menu.IsOpen = true;
        }
    }

    private void OnShowNativeMenuClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var paths = vm.SelectedItems.Select(i => i.Entry.FullPath).ToArray();
        if (paths.Length > 0)
        {
            ContextMenuService.ShowMenu(paths, (int)_lastItemMenuScreenPoint.X, (int)_lastItemMenuScreenPoint.Y);
        }
    }

    private void OnShowPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedItems: [{ } first, ..] })
        {
            ContextMenuService.ShowProperties(first.Entry.FullPath);
        }
    }

    /// <summary>키보드 페인 전환 시 호출 — 선택된 행(없으면 목록)에 포커스를 준다.</summary>
    public void FocusList()
    {
        if (FileList.SelectedItem is { } selected
            && FileList.ItemContainerGenerator.ContainerFromItem(selected) is ListViewItem container)
        {
            container.Focus();
            return;
        }

        FileList.Focus();
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_suppressBackgroundMenuOnce)
        {
            _suppressBackgroundMenuOnce = false;
            e.Handled = true;
        }
    }

    // ─── 인라인 이름 변경 ────────────────────────────────────────────────────────

    private void OnRenameBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box
            || box.DataContext is not FileItemViewModel { IsRenaming: true } item)
        {
            return;
        }

        box.Focus();

        // 확장자를 제외한 이름 부분만 선택한다 (탐색기 동작)
        var dotIndex = item.IsDirectory ? -1 : box.Text.LastIndexOf('.');
        box.Select(0, dotIndex > 0 ? dotIndex : box.Text.Length);
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box
            || box.DataContext is not FileItemViewModel item
            || ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.CommitRenameCommand.Execute(item);
            FileList.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelRenameCommand.Execute(item);
            FileList.Focus();
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox { DataContext: FileItemViewModel { IsRenaming: true } item }
            && ViewModel is { } vm)
        {
            vm.CommitRenameCommand.Execute(item);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
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
