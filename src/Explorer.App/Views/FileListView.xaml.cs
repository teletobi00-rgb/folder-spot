using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Explorer.App.ViewModels;
using Explorer.Core.Sorting;

namespace Explorer.App.Views;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private FileListViewModel? ViewModel => DataContext as FileListViewModel;

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

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
