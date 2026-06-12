using Explorer.Shell.Threading;
using FluentAssertions;

namespace Explorer.Shell.Tests.Threading;

public sealed class StaWorkerTests
{
    [Fact]
    public async Task RunAsync_ExecutesOnStaThread()
    {
        using var worker = new StaWorker("test-sta");

        var apartment = await worker.RunAsync(() => Thread.CurrentThread.GetApartmentState());

        apartment.Should().Be(ApartmentState.STA);
    }

    [Fact]
    public async Task RunAsync_RunsSequentiallyOnSameThread()
    {
        using var worker = new StaWorker("test-sta");

        var first = await worker.RunAsync(() => Environment.CurrentManagedThreadId);
        var second = await worker.RunAsync(() => Environment.CurrentManagedThreadId);

        second.Should().Be(first);
    }

    [Fact]
    public async Task RunAsync_PropagatesExceptions()
    {
        using var worker = new StaWorker("test-sta");

        var act = () => worker.RunAsync<int>(() => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var worker = new StaWorker("test-sta");
        worker.Dispose();

        var act = () => worker.RunAsync(() => 1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
