using System.Threading.Channels;
using Explorer.Core.FileOperations;
using Microsoft.Extensions.Logging;

namespace Explorer.Core.Operations;

public enum OperationState
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled,
}

/// <summary>큐에 들어간 작업 한 건. 상태/진행은 큐 워커가 갱신하고 이벤트로 알린다.</summary>
public sealed class QueuedOperation
{
    internal QueuedOperation(OperationRequest request)
    {
        Request = request;
        Description = request.Describe();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public OperationRequest Request { get; }

    public string Description { get; }

    public OperationControl Control { get; } = new();

    public OperationState State { get; internal set; } = OperationState.Queued;

    public OperationProgress Progress { get; private set; } = OperationProgress.Empty;

    public FileOperationResult? Result { get; internal set; }

    /// <summary>진행 갱신 시 (보고한 스레드에서 — UI 구독자는 마샬링 필요).</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>실행기가 진행 상태를 보고한다.</summary>
    public void ReportProgress(OperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Progress = progress;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    internal TaskCompletionSource<FileOperationResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>큐 워커가 작업 한 건을 실제로 실행한다 (충돌 해소 + 서비스 호출 + Undo 기록).</summary>
public interface IQueuedOperationExecutor
{
    Task<FileOperationResult> ExecuteAsync(QueuedOperation operation);
}

/// <summary>
/// 파일 작업 직렬 실행 큐. 이벤트는 워커 스레드에서 발생하므로 UI 구독자는 마샬링해야 한다.
/// </summary>
public interface IOperationQueue
{
    /// <summary>현재 큐 내용 스냅샷 (완료 항목 포함, 추가 순서).</summary>
    IReadOnlyList<QueuedOperation> Snapshot { get; }

    /// <summary>항목 추가/제거 시.</summary>
    event EventHandler? QueueChanged;

    /// <summary>항목의 상태/진행 변경 시.</summary>
    event EventHandler<QueuedOperation>? OperationUpdated;

    /// <summary>작업을 큐에 넣고, 그 작업이 끝났을 때 완료되는 Task를 돌려준다.</summary>
    Task<FileOperationResult> EnqueueAsync(OperationRequest request);

    /// <summary>끝난 항목(완료/실패/취소)을 목록에서 제거한다.</summary>
    void ClearFinished();
}

public sealed class OperationQueue : IOperationQueue, IDisposable
{
    private readonly IQueuedOperationExecutor _executor;
    private readonly ILogger<OperationQueue> _logger;
    private readonly Channel<QueuedOperation> _channel = Channel.CreateUnbounded<QueuedOperation>();
    private readonly List<QueuedOperation> _operations = [];
    private readonly Lock _gate = new();
    private readonly Task _workerTask;

    public OperationQueue(IQueuedOperationExecutor executor, ILogger<OperationQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(logger);
        _executor = executor;
        _logger = logger;
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public event EventHandler? QueueChanged;

    public event EventHandler<QueuedOperation>? OperationUpdated;

    public IReadOnlyList<QueuedOperation> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return [.. _operations];
            }
        }
    }

    public Task<FileOperationResult> EnqueueAsync(OperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = new QueuedOperation(request);
        operation.ProgressChanged += (_, _) => NotifyUpdated(operation);
        operation.Control.StateChanged += (_, _) => NotifyUpdated(operation);
        lock (_gate)
        {
            _operations.Add(operation);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);

        if (!_channel.Writer.TryWrite(operation))
        {
            operation.State = OperationState.Failed;
            operation.Result = FileOperationResult.Failure(FileOperationError.Unknown, "작업 큐가 닫혔습니다.");
            operation.Completion.TrySetResult(operation.Result);
        }

        return operation.Completion.Task;
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            _operations.RemoveAll(o => o.State
                is OperationState.Completed or OperationState.Failed or OperationState.Canceled);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>워커/항목이 상태나 진행을 바꿨을 때 알림 (실행기에서도 호출).</summary>
    public void NotifyUpdated(QueuedOperation operation) => OperationUpdated?.Invoke(this, operation);

    public void Dispose()
    {
        _channel.Writer.TryComplete();

        // 진행/일시정지 중인 작업이 셧다운을 붙잡지 않도록 먼저 취소를 요청한다.
        foreach (var operation in Snapshot)
        {
            if (operation.State is OperationState.Queued or OperationState.Running)
            {
                operation.Control.Cancel();
            }
        }

        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var operation in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await RunOneAsync(operation).ConfigureAwait(false);
        }
    }

    private async Task RunOneAsync(QueuedOperation operation)
    {
        if (operation.Control.IsCancellationRequested)
        {
            Finish(operation, FileOperationResult.Cancelled());
            return;
        }

        operation.State = OperationState.Running;
        NotifyUpdated(operation);

        FileOperationResult result;
        try
        {
            result = await _executor.ExecuteAsync(operation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 실행기는 결과로 보고하는 게 계약이지만, 어떤 예외도 큐 워커를 죽여선 안 된다.
            _logger.LogError(ex, "큐 작업 실행 중 예외: {Description}", operation.Description);
            result = FileOperationResult.Failure(FileOperationErrorMapper.FromException(ex), ex.Message);
        }

        Finish(operation, result);
    }

    private void Finish(QueuedOperation operation, FileOperationResult result)
    {
        operation.Result = result;
        operation.State = result.Succeeded
            ? OperationState.Completed
            : result.Aborted ? OperationState.Canceled : OperationState.Failed;
        NotifyUpdated(operation);
        operation.Completion.TrySetResult(result);
    }
}
