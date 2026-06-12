using System.Collections.Concurrent;

namespace Explorer.Shell.Threading;

/// <summary>
/// 셸 COM 호출(SHGetFileInfo 등)용 전용 STA 스레드. 스레드풀(MTA)에서 호출 시
/// 일부 셸 확장이 조용히 실패하는 문제를 피한다. 작업은 순차 처리된다.
/// </summary>
public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public StaWorker(string threadName = "Explorer.StaWorker")
    {
        _thread = new Thread(ProcessQueue)
        {
            Name = threadName,
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            completion.SetException(new ObjectDisposedException(nameof(StaWorker), ex));
        }

        return completion.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }

    private void ProcessQueue()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }

        _queue.Dispose();
    }
}
