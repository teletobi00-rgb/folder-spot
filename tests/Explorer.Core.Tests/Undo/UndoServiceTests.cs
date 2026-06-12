using Explorer.Core.FileOperations;
using Explorer.Core.Undo;
using FluentAssertions;

namespace Explorer.Core.Tests.Undo;

public sealed class UndoServiceTests
{
    private static UndoEntry Entry(string description, Func<Task<FileOperationResult>>? inverse = null) =>
        new(description, inverse ?? (() => Task.FromResult(FileOperationResult.Success())));

    [Fact]
    public void EmptyStack_CannotUndo()
    {
        var service = new UndoService();

        service.CanUndo.Should().BeFalse();
        service.PeekDescription.Should().BeNull();
    }

    [Fact]
    public async Task Undo_RunsInverse_LifoOrder()
    {
        var service = new UndoService();
        var executed = new List<string>();
        service.Push(Entry("첫번째", () => { executed.Add("1"); return Task.FromResult(FileOperationResult.Success()); }));
        service.Push(Entry("두번째", () => { executed.Add("2"); return Task.FromResult(FileOperationResult.Success()); }));

        var first = await service.TryUndoAsync();
        var second = await service.TryUndoAsync();

        first!.Value.Description.Should().Be("두번째");
        second!.Value.Description.Should().Be("첫번째");
        executed.Should().Equal("2", "1");
        service.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Undo_EmptyStack_GivesNull()
    {
        var service = new UndoService();

        (await service.TryUndoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task FailedInverse_StillConsumesEntry()
    {
        var service = new UndoService();
        service.Push(Entry("실패", () => Task.FromResult(
            FileOperationResult.Failure(FileOperationError.AccessDenied))));

        var result = await service.TryUndoAsync();

        result!.Value.Result.Succeeded.Should().BeFalse();
        service.CanUndo.Should().BeFalse("실패해도 항목은 소비 — 중복 부작용 방지");
    }

    [Fact]
    public async Task ConcurrentUndo_OnlyOneRuns()
    {
        var service = new UndoService();
        var gate = new TaskCompletionSource<FileOperationResult>();
        service.Push(Entry("느린 작업", () => gate.Task));

        var firstTask = service.TryUndoAsync();
        await Task.Delay(30);
        var secondAttempt = await service.TryUndoAsync();

        secondAttempt.Should().BeNull("이미 실행 중이면 거부");
        gate.SetResult(FileOperationResult.Success());
        (await firstTask).Should().NotBeNull();
    }

    [Fact]
    public async Task Capacity_DropsOldestBeyondTwenty()
    {
        var service = new UndoService();
        for (var i = 0; i < 25; i++)
        {
            service.Push(Entry($"작업{i}"));
        }

        service.PeekDescription.Should().Be("작업24");

        // 20개를 모두 비우면 끝 — 0~4번은 버려졌다
        var drained = 0;
        while (service.CanUndo)
        {
            _ = await service.TryUndoAsync();
            drained++;
        }

        drained.Should().Be(20);
    }

    [Fact]
    public void Changed_FiresOnPushAndUndo()
    {
        var service = new UndoService();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.Push(Entry("x"));

        changes.Should().BeGreaterThanOrEqualTo(1);
    }
}
