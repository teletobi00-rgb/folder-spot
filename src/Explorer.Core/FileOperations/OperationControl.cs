namespace Explorer.Core.FileOperations;

/// <summary>
/// 실행 중인 파일 작업의 일시정지/재개/취소 제어.
/// <see cref="WaitIfPaused"/>는 작업 엔진 스레드가 호출해 일시정지 동안 블로킹된다.
/// </summary>
public sealed class OperationControl : IDisposable
{
    private readonly ManualResetEventSlim _resumeGate = new(initialState: true);

    // UI 스레드가 쓰고 작업 엔진 스레드가 읽는다 — 약한 메모리 모델(ARM)에서도 보이도록 volatile.
    private volatile bool _isPaused;
    private volatile bool _isCancellationRequested;

    public bool IsPaused => _isPaused;

    public bool IsCancellationRequested => _isCancellationRequested;

    /// <summary>Pause/Resume/Cancel 호출 시 발생 (호출자 스레드에서).</summary>
    public event EventHandler? StateChanged;

    public void Pause()
    {
        if (_isPaused || _isCancellationRequested)
        {
            return;
        }

        _isPaused = true;
        _resumeGate.Reset();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (!_isPaused)
        {
            return;
        }

        _isPaused = false;
        _resumeGate.Set();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        if (_isCancellationRequested)
        {
            return;
        }

        _isCancellationRequested = true;
        _isPaused = false;
        _resumeGate.Set(); // 일시정지 중이라도 취소는 즉시 풀려서 엔진이 취소를 인지하게 한다
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>작업 엔진 스레드 전용 — 일시정지 상태면 재개/취소까지 블로킹.</summary>
    public void WaitIfPaused() => _resumeGate.Wait();

    public void Dispose() => _resumeGate.Dispose();
}
