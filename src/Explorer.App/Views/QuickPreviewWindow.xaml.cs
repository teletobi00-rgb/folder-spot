using System.Windows;
using System.Windows.Input;
using Explorer.App.ViewModels;
using Explorer.Preview;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

/// <summary>Space 퀵 프리뷰 오버레이 — 선택 항목을 즉시 렌더(디바운스 없음)하고 Space/Esc로 닫는다.</summary>
public partial class QuickPreviewWindow : FluentWindow
{
    private readonly IPreviewRendererRegistry _registry;
    private readonly PreviewViewModel _viewModel = new();
    private int _generation;

    public QuickPreviewWindow(IPreviewRendererRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        DataContext = _viewModel;
        InitializeComponent();
    }

    /// <summary>경로의 미리보기를 즉시 렌더하고 창을 띄운다. 이미 떠 있으면 닫는다(토글).</summary>
    public async void Toggle(string? filePath)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        // 연속 호출 시 직전 결과를 버리기 위한 세대 카운터(렌더는 짧으므로 취소 토큰까지는 불필요).
        var generation = ++_generation;
        Show();
        Activate();
        _viewModel.IsLoading = true;

        var result = await _registry.RenderAsync(filePath, CancellationToken.None).ConfigureAwait(true);
        if (generation == _generation)
        {
            _viewModel.IsLoading = false;
            _viewModel.Apply(result);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Space)
        {
            e.Handled = true;
            HideAndClear();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => HideAndClear();

    /// <summary>숨길 때 미리보기를 비운다 — Kind=None으로 바뀌며 PreviewView가 재생 중인 미디어를 정지한다.</summary>
    private void HideAndClear()
    {
        Hide();
        _generation++; // 진행 중인 렌더 결과 폐기
        _viewModel.Clear();
    }
}
