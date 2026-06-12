using Explorer.Core.FileOperations;
using FluentAssertions;

namespace Explorer.Core.Tests.FileOperations;

public sealed class OperationControlTests
{
    [Fact]
    public async Task Pause_BlocksWaiter_ResumeReleases()
    {
        using var control = new OperationControl();
        control.Pause();

        var waiter = Task.Run(() =>
        {
            control.WaitIfPaused();
            return true;
        });

        var winner = await Task.WhenAny(waiter, Task.Delay(150));
        winner.Should().NotBe(waiter, "일시정지 중에는 블로킹");

        control.Resume();
        (await waiter.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue("재개 후 즉시 풀림");
    }

    [Fact]
    public async Task Cancel_WhilePaused_ReleasesWaiterAndFlagsCancellation()
    {
        using var control = new OperationControl();
        control.Pause();
        var waiter = Task.Run(control.WaitIfPaused);

        control.Cancel();

        await waiter.WaitAsync(TimeSpan.FromSeconds(2));
        control.IsCancellationRequested.Should().BeTrue();
        control.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void Pause_AfterCancel_IsIgnored()
    {
        using var control = new OperationControl();
        control.Cancel();

        control.Pause();

        control.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void StateChanged_FiresOnTransitionsOnly()
    {
        using var control = new OperationControl();
        var events = 0;
        control.StateChanged += (_, _) => events++;

        control.Pause();
        control.Pause(); // 무시
        control.Resume();
        control.Resume(); // 무시
        control.Cancel();
        control.Cancel(); // 무시

        events.Should().Be(3);
    }
}
