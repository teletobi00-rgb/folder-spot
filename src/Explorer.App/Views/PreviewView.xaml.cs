using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Explorer.App.ViewModels;
using Explorer.Preview;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Explorer.App.Views;

public partial class PreviewView : UserControl
{
    public PreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PreviewViewModel? ViewModel => DataContext as PreviewViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PreviewViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is PreviewViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyResult(newVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreviewViewModel.Kind) or nameof(PreviewViewModel.Result)
            && ViewModel is { } vm)
        {
            ApplyResult(vm);
        }
    }

    /// <summary>AvalonEdit와 MediaElement는 바인딩이 까다로워 코드비하인드에서 직접 갱신한다.</summary>
    private void ApplyResult(PreviewViewModel vm)
    {
        if (vm.Kind == PreviewKind.Text)
        {
            TextView.Text = vm.Result.Text ?? string.Empty;
            TextView.SyntaxHighlighting = vm.Result.LanguageHint is { } lang
                ? HighlightingManager.Instance.GetDefinition(lang)
                : null;
            TextView.ScrollToHome();
        }
        else
        {
            TextView.Text = string.Empty;
        }

        if (vm.Kind == PreviewKind.Media && vm.MediaSource is { } uri)
        {
            MediaView.Source = uri;
        }
        else
        {
            MediaView.Stop();
            MediaView.Source = null;
        }
    }

    private void OnMediaPlay(object sender, RoutedEventArgs e) => MediaView.Play();

    private void OnMediaPause(object sender, RoutedEventArgs e) => MediaView.Pause();

    private void OnMediaStop(object sender, RoutedEventArgs e) => MediaView.Stop();
}
