using Explorer.Core.FileOperations;
using Explorer.Core.Operations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Core.Tests.Operations;

public sealed class OperationQueueTests
{
    private sealed class FakeExecutor : IQueuedOperationExecutor
    {
        private readonly Func<QueuedOperation, Task<FileOperationResult>> _handler;

        public FakeExecutor(Func<QueuedOperation, Task<FileOperationResult>>? handler = null)
        {
            _handler = handler ?? (_ => Task.FromResult(FileOperationResult.Success()));
        }

        public List<QueuedOperation> Executed { get; } = [];

        public Task<FileOperationResult> ExecuteAsync(QueuedOperation operation)
        {
            Executed.Add(operation);
            return _handler(operation);
        }
    }

    private static OperationRequest Request(string name = "a.txt") =>
        OperationRequest.Copy([@"C:\src\" + name], @"C:\dst");

    [Fact]
    public async Task Enqueue_RunsAndCompletes()
    {
        var executor = new FakeExecutor();
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);

        var result = await queue.EnqueueAsync(Request());

        result.Succeeded.Should().BeTrue();
        var op = queue.Snapshot.Should().ContainSingle().Subject;
        op.State.Should().Be(OperationState.Completed);
        executor.Executed.Should().HaveCount(1);
    }

    [Fact]
    public async Task Operations_RunSequentially_InFifoOrder()
    {
        var running = 0;
        var maxConcurrent = 0;
        var order = new List<string>();
        var executor = new FakeExecutor(async op =>
        {
            var now = Interlocked.Increment(ref running);
            maxConcurrent = Math.Max(maxConcurrent, now);
            order.Add(op.Request.Sources[0]);
            await Task.Delay(30);
            Interlocked.Decrement(ref running);
            return FileOperationResult.Success();
        });
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);

        var tasks = new[] { Request("1"), Request("2"), Request("3") }
            .Select(queue.EnqueueAsync).ToArray();
        await Task.WhenAll(tasks);

        maxConcurrent.Should().Be(1, "직렬 실행");
        order.Should().Equal(@"C:\src\1", @"C:\src\2", @"C:\src\3");
    }

    [Fact]
    public async Task CancelWhileQueued_SkipsExecution()
    {
        var firstStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var executor = new FakeExecutor(async _ =>
        {
            firstStarted.TrySetResult();
            await release.Task;
            return FileOperationResult.Success();
        });
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);

        var first = queue.EnqueueAsync(Request("first"));
        await firstStarted.Task;
        var second = queue.EnqueueAsync(Request("second"));
        queue.Snapshot[1].Control.Cancel();
        release.SetResult();

        await Task.WhenAll(first, second);

        (await second).Aborted.Should().BeTrue();
        queue.Snapshot[1].State.Should().Be(OperationState.Canceled);
        executor.Executed.Should().HaveCount(1, "취소된 작업은 실행기로 가지 않는다");
    }

    [Fact]
    public async Task ExecutorFailure_MarksFailed_AndKeepsWorkerAlive()
    {
        var first = true;
        var executor = new FakeExecutor(_ =>
        {
            if (first)
            {
                first = false;
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(FileOperationResult.Success());
        });
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);

        var failed = await queue.EnqueueAsync(Request("bad"));
        var ok = await queue.EnqueueAsync(Request("good"));

        failed.Succeeded.Should().BeFalse();
        ok.Succeeded.Should().BeTrue("실행기 예외가 워커를 죽이면 안 된다");
        queue.Snapshot[0].State.Should().Be(OperationState.Failed);
        queue.Snapshot[1].State.Should().Be(OperationState.Completed);
    }

    [Fact]
    public async Task ClearFinished_RemovesOnlyFinishedItems()
    {
        var blocker = new TaskCompletionSource<FileOperationResult>();
        var calls = 0;
        var executor = new FakeExecutor(_ =>
            Interlocked.Increment(ref calls) == 1
                ? Task.FromResult(FileOperationResult.Success())
                : blocker.Task);
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);

        await queue.EnqueueAsync(Request("done"));
        var pending = queue.EnqueueAsync(Request("running"));
        await Task.Delay(50);

        queue.ClearFinished();

        queue.Snapshot.Should().ContainSingle().Which.Request.Sources[0].Should().Be(@"C:\src\running");
        blocker.SetResult(FileOperationResult.Success());
        await pending;
    }

    [Fact]
    public async Task Events_FireOnAddAndStateChanges()
    {
        var executor = new FakeExecutor();
        using var queue = new OperationQueue(executor, NullLogger<OperationQueue>.Instance);
        var queueChanged = 0;
        var updates = new List<OperationState>();
        queue.QueueChanged += (_, _) => queueChanged++;
        queue.OperationUpdated += (_, op) => updates.Add(op.State);

        await queue.EnqueueAsync(Request());

        queueChanged.Should().BeGreaterThanOrEqualTo(1);
        updates.Should().Contain(OperationState.Running);
        updates.Should().Contain(OperationState.Completed);
    }
}
