namespace Explorer.Preview;

/// <summary>
/// 활성 페인 선택 변화를 디바운스(기본 250ms) + 취소 토큰으로 추적해 미리보기를 생성한다.
/// 빠른 커서 이동 시 직전 요청을 취소해 잰크를 막는다. UI 스레드에서 호출하고 이벤트도 UI에서 발생한다.
/// </summary>
public sealed class PreviewCoordinator : IDisposable
{
    private readonly IPreviewRendererRegistry _registry;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _cts;

    public PreviewCoordinator(IPreviewRendererRegistry registry, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>미리보기 생성 완료 (취소되면 발생하지 않음).</summary>
    public event EventHandler<PreviewResult>? PreviewReady;

    /// <summary>로딩 시작/종료 상태 변화.</summary>
    public event EventHandler<bool>? LoadingChanged;

    /// <summary>경로의 미리보기를 요청한다. null/빈 값/폴더면 즉시 비운다.</summary>
    public async void Request(string? filePath)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        previous?.Cancel();
        previous?.Dispose();
        var ct = cts.Token;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            LoadingChanged?.Invoke(this, false);
            PreviewReady?.Invoke(this, PreviewResult.None());
            return;
        }

        try
        {
            LoadingChanged?.Invoke(this, true);
            await Task.Delay(_debounce, ct).ConfigureAwait(true);

            var result = await _registry.RenderAsync(filePath, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            LoadingChanged?.Invoke(this, false);
            PreviewReady?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            // 다음 요청이 이어받는다 — 로딩 상태는 그 요청이 관리한다.
        }
    }

    /// <summary>현재 요청을 취소하고 미리보기를 비운다.</summary>
    public void Clear() => Request(null);

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
