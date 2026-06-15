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

    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        if (ViewModel is not null)
        {
            ViewModel.SelectedItems = FileList.SelectedItems.Cast<FileItemViewModel>().ToArray();
        }
    }

    // ─── 드래그 시작 (앱 → 탐색기/다른 곳) ─────────────────────────────────────────

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);

        // Shift/Ctrl 클릭은 다중 선택 제스처 — 드래그 시작으로 취급하면 선택 확장이 깨진다.
        _dragStartedOnItem = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0
            && e.OriginalSource is DependencyObject source
            && e.OriginalSource is not System.Windows.Controls.TextBox
            && FindAncestor<ListViewItem>(source) is not null;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragStartedOnItem
            || e.LeftButton != MouseButtonState.Pressed
            || ViewModel is not { SelectedItems.Count: > 0 } vm)
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
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
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
