using Explorer.App.Services.Operations;
using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using Explorer.Core.Operations;
using Explorer.Core.Settings;
using Explorer.Core.Undo;
using Explorer.Preview;
using Explorer.Preview.Renderers;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.TestSupport;

/// <summary>
/// FileListViewModel 조립용 공용 픽스처 — 열거기/설정/작업/즐겨찾기/큐는 두 페인이 공유,
/// 와처/클립보드 등은 인스턴스별 mock. 큐는 실제 구현(직렬 워커+실행기)을 쓴다.
/// </summary>
internal sealed class FileListTestContext
{
    public IFileSystemEnumerator Enumerator { get; } = Substitute.For<IFileSystemEnumerator>();

    public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();

    public IFileOperationService Operations { get; } = Substitute.For<IFileOperationService>();

    public IFavoritesService Favorites { get; } = Substitute.For<IFavoritesService>();

    public IConflictPrompt ConflictPrompt { get; } = Substitute.For<IConflictPrompt>();

    public IUndoService Undo { get; } = new UndoService();

    public IOperationQueue Queue { get; }

    /// <summary>실제 레지스트리(Info 폴백만) — 미리보기 통합 테스트용.</summary>
    public IPreviewRendererRegistry PreviewRegistry { get; } = new PreviewRendererRegistry(
        [new InfoPreviewRenderer()], NullLogger<PreviewRendererRegistry>.Instance);

    public FileListTestContext()
    {
        Settings.Current.Returns(new AppSettings());
        Enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>([]));
        StreamingEnumeratorStub.StreamFromList(Enumerator);

        var success = Task.FromResult(FileOperationResult.Success());
        Operations.CopyAsync(default!, default!, default).ReturnsForAnyArgs(success);
        Operations.MoveAsync(default!, default!, default).ReturnsForAnyArgs(success);
        Operations.DeleteAsync(default!, default, default).ReturnsForAnyArgs(success);
        Operations.RenameAsync(default!, default!).ReturnsForAnyArgs(success);
        Operations.CreateFolderAsync(default!, default!).ReturnsForAnyArgs(success);
        Operations.MoveItemAsync(default!, default!, default).ReturnsForAnyArgs(success);

        Queue = new OperationQueue(
            new QueuedOperationExecutor(Operations, ConflictPrompt, Undo, NullLogger<QueuedOperationExecutor>.Instance),
            NullLogger<OperationQueue>.Instance);
    }

    public FileListViewModel CreateFileList() => new(
        Enumerator,
        Substitute.For<IFileLauncher>(),
        Settings,
        Substitute.For<IShellIconProvider>(),
        Operations,
        Substitute.For<IFileClipboardService>(),
        Substitute.For<IFolderWatcher>(),
        Favorites,
        Queue,
        NullLogger<FileListViewModel>.Instance);

    public static async Task WaitUntilAsync(Func<bool> condition, string because = "")
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }
}
