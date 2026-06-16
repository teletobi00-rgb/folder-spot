using Explorer.App.Services.Operations;
using Explorer.Core.FileOperations;
using Explorer.Core.Operations;
using Explorer.Core.Undo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.Services;

public sealed class QueuedOperationExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _destDir;
    private readonly IFileOperationService _operations = Substitute.For<IFileOperationService>();
    private readonly IConflictPrompt _prompt = Substitute.For<IConflictPrompt>();
    private readonly UndoService _undo = new();
    private readonly QueuedOperationExecutor _executor;

    public QueuedOperationExecutorTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        _sourceDir = System.IO.Path.Combine(_tempDir, "src");
        _destDir = System.IO.Path.Combine(_tempDir, "dst");
        System.IO.Directory.CreateDirectory(_sourceDir);
        System.IO.Directory.CreateDirectory(_destDir);

        var success = Task.FromResult(FileOperationResult.Success());
        _operations.CopyAsync(default!, default!, default).ReturnsForAnyArgs(success);
        _operations.MoveAsync(default!, default!, default).ReturnsForAnyArgs(success);
        _operations.DeleteAsync(default!, default, default).ReturnsForAnyArgs(success);
        _operations.MoveItemAsync(default!, default!, default).ReturnsForAnyArgs(success);

        _executor = new QueuedOperationExecutor(
            _operations, _prompt, _undo, NullLogger<QueuedOperationExecutor>.Instance);
    }

    public void Dispose()
    {
        if (!System.IO.Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            System.IO.Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }

    private string MakeSource(string name)
    {
        var path = System.IO.Path.Combine(_sourceDir, name);
        System.IO.File.WriteAllText(path, "src");
        return path;
    }

    private string MakeDestCollision(string name)
    {
        var path = System.IO.Path.Combine(_destDir, name);
        System.IO.File.WriteAllText(path, "dst");
        return path;
    }

    private static QueuedOperation Op(OperationRequest request)
    {
        // QueuedOperation 생성자는 큐 내부 전용(internal) — 시그니처를 명시해 리플렉션으로 만든다.
        var ctor = typeof(QueuedOperation).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            [typeof(OperationRequest)])
            ?? throw new InvalidOperationException("QueuedOperation(OperationRequest) 생성자를 찾을 수 없습니다.");
        return (QueuedOperation)ctor.Invoke([request]);
    }

    [Fact]
    public async Task NoConflicts_ExecutesSingleDefaultGroup_WithoutPrompt()
    {
        var source = MakeSource("clean.txt");

        var result = await _executor.ExecuteAsync(Op(OperationRequest.Copy([source], _destDir)));

        result.Succeeded.Should().BeTrue();
        await _prompt.DidNotReceiveWithAnyArgs().ResolveAsync(default!);
        await _operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == source),
            _destDir,
            Arg.Is<FileOperationContext?>(c => c!.Collision == CollisionOption.Default));
    }

    [Fact]
    public async Task ConflictDecisions_SplitIntoGroups()
    {
        var clean = MakeSource("clean.txt");
        var skipMe = MakeSource("skip.txt");
        MakeDestCollision("skip.txt");
        var keepMe = MakeSource("keep.txt");
        MakeDestCollision("keep.txt");
        var overwriteMe = MakeSource("overwrite.txt");
        MakeDestCollision("overwrite.txt");

        _prompt.ResolveAsync(Arg.Any<IReadOnlyList<FileConflict>>())
            .Returns(call =>
            {
                var conflicts = call.Arg<IReadOnlyList<FileConflict>>();
                var decisions = new Dictionary<FileConflict, ConflictDecision>();
                foreach (var conflict in conflicts)
                {
                    decisions[conflict] = conflict.Name switch
                    {
                        "skip.txt" => ConflictDecision.Skip,
                        "keep.txt" => ConflictDecision.KeepBoth,
                        _ => ConflictDecision.Overwrite,
                    };
                }

                return Task.FromResult<IReadOnlyDictionary<FileConflict, ConflictDecision>?>(decisions);
            });

        var result = await _executor.ExecuteAsync(
            Op(OperationRequest.Copy([clean, skipMe, keepMe, overwriteMe], _destDir)));

        result.Succeeded.Should().BeTrue();
        await _operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == clean),
            _destDir,
            Arg.Is<FileOperationContext?>(c => c!.Collision == CollisionOption.Default));
        await _operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == overwriteMe),
            _destDir,
            Arg.Is<FileOperationContext?>(c => c!.Collision == CollisionOption.Overwrite));
        await _operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == keepMe),
            _destDir,
            Arg.Is<FileOperationContext?>(c => c!.Collision == CollisionOption.KeepBoth));
    }

    [Fact]
    public async Task PromptCancelled_AbortsWithoutServiceCall()
    {
        var source = MakeSource("dup.txt");
        MakeDestCollision("dup.txt");
        _prompt.ResolveAsync(Arg.Any<IReadOnlyList<FileConflict>>())
            .Returns(Task.FromResult<IReadOnlyDictionary<FileConflict, ConflictDecision>?>(null));

        var result = await _executor.ExecuteAsync(Op(OperationRequest.Copy([source], _destDir)));

        result.Aborted.Should().BeTrue();
        await _operations.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default);
    }

    [Fact]
    public async Task AllSkipped_SucceedsWithoutServiceCall()
    {
        var source = MakeSource("dup.txt");
        MakeDestCollision("dup.txt");
        _prompt.ResolveAsync(Arg.Any<IReadOnlyList<FileConflict>>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<FileConflict, ConflictDecision>?>(
                call.Arg<IReadOnlyList<FileConflict>>().ToDictionary(c => c, _ => ConflictDecision.Skip)));

        var result = await _executor.ExecuteAsync(Op(OperationRequest.Copy([source], _destDir)));

        result.Succeeded.Should().BeTrue();
        await _operations.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default);
    }

    [Fact]
    public async Task MoveSuccess_PushesUndo_ThatMovesItemsBack()
    {
        var source = MakeSource("moved.txt");
        var newPath = System.IO.Path.Combine(_destDir, "moved.txt");
        _operations.MoveAsync(default!, default!, default).ReturnsForAnyArgs(call =>
        {
            call.Arg<FileOperationContext?>()?.Events?.OnItemCompleted(new CompletedItem
            {
                Kind = OperationKind.Move,
                SourcePath = source,
                NewPath = newPath,
            });
            return Task.FromResult(FileOperationResult.Success());
        });

        await _executor.ExecuteAsync(Op(OperationRequest.Move([source], _destDir)));

        _undo.CanUndo.Should().BeTrue();
        var undone = await _undo.TryUndoAsync();
        undone!.Value.Result.Succeeded.Should().BeTrue();
        await _operations.Received(1).MoveItemAsync(newPath, _sourceDir, "moved.txt");
    }

    [Fact]
    public async Task RecycleDelete_PushesUndo_ThatRestoresFromRecyclePath()
    {
        var source = MakeSource("deleted.txt");
        var recyclePath = @"C:\$Recycle.Bin\S-1-5-21\$R1234.txt";
        _operations.DeleteAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            call.Arg<FileOperationContext?>()?.Events?.OnItemCompleted(new CompletedItem
            {
                Kind = OperationKind.Delete,
                SourcePath = source,
                NewPath = recyclePath,
            });
            return Task.FromResult(FileOperationResult.Success());
        });

        await _executor.ExecuteAsync(Op(OperationRequest.Delete([source], permanent: false)));

        _undo.CanUndo.Should().BeTrue();
        _ = await _undo.TryUndoAsync();
        await _operations.Received(1).MoveItemAsync(recyclePath, _sourceDir, "deleted.txt");
    }

    [Fact]
    public async Task PermanentDelete_DoesNotPushUndo()
    {
        var source = MakeSource("gone.txt");

        await _executor.ExecuteAsync(Op(OperationRequest.Delete([source], permanent: true)));

        _undo.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task CopySuccess_PushesUndo_ThatDeletesCreatedCopies()
    {
        var source = MakeSource("copied.txt");
        var newPath = System.IO.Path.Combine(_destDir, "copied.txt");
        _operations.CopyAsync(default!, default!, default).ReturnsForAnyArgs(call =>
        {
            call.Arg<FileOperationContext?>()?.Events?.OnItemCompleted(new CompletedItem
            {
                Kind = OperationKind.Copy,
                SourcePath = source,
                NewPath = newPath,
            });
            return Task.FromResult(FileOperationResult.Success());
        });

        await _executor.ExecuteAsync(Op(OperationRequest.Copy([source], _destDir)));

        _ = await _undo.TryUndoAsync();
        await _operations.Received(1).DeleteAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == newPath),
            false,
            Arg.Any<FileOperationContext?>());
    }
}
