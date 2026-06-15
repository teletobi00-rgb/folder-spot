using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Explorer.App.ViewModels;
using Explorer.App.Views.Controls;
using Explorer.Preview;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Explorer.App.Views;

public partial class PreviewView : UserControl
{
    private PreviewHandlerHost? _nativeHost;

    public PreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 트리에서 제거되면 네이티브 미리보기 호스트(COM 핸들러/HWND/prevhost.exe)를 즉시 정리한다.
        // GC 파이널라이저에 맡기면 대리 프로세스가 늦게까지 살아있는다. 재표시되면 ApplyResult가 재생성.
        Unloaded += (_, _) =>
        {
            _nativeHost?.Dispose();
            _nativeHost = null;
        };
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

        if (vm.Kind == PreviewKind.Native)
        {
            _nativeHost ??= new PreviewHandlerHost();
            if (!ReferenceEquals(NativeHost.Child, _nativeHost))
            {
                NativeHost.Child = _nativeHost;
            }

            _nativeHost.FilePath = vm.Result.FilePath;
        }
        else if (_nativeHost is not null)
        {
            _nativeHost.FilePath = null; // 핸들러 언로드 (HWND는 재사용)
        }
    }

    private void OnMediaPlay(object sender, RoutedEventArgs e) => MediaView.Play();

    private void OnMediaPause(object sender, RoutedEventArgs e) => MediaView.Pause();

    private void OnMediaStop(object sender, RoutedEventArgs e) => MediaView.Stop();
}
