using System.IO;
using Explorer.App.Services.Operations;
using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Operations;
using Explorer.Core.Settings;
using Explorer.Core.Undo;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class FileListViewModelOperationTests
{
    private readonly IFileSystemEnumerator _enumerator = Substitute.For<IFileSystemEnumerator>();
    private readonly IFileOperationService _operations = Substitute.For<IFileOperationService>();
    private readonly IFileClipboardService _clipboard = Substitute.For<IFileClipboardService>();
    private readonly IFolderWatcher _watcher = Substitute.For<IFolderWatcher>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IOperationQueue _queue;

    public FileListViewModelOperationTests()
    {
        _settings.Current.Returns(new AppSettings());
        _operations.CopyAsync(default!, default!, default).ReturnsForAnyArgs(FileOperationResult.Success());
        _operations.MoveAsync(default!, default!, default).ReturnsForAnyArgs(FileOperationResult.Success());
        _operations.DeleteAsync(default!, default, default).ReturnsForAnyArgs(FileOperationResult.Success());
        _operations.RenameAsync(default!, default!).ReturnsForAnyArgs(FileOperationResult.Success());
        _operations.CreateFolderAsync(default!, default!).ReturnsForAnyArgs(FileOperationResult.Success());

        // 실제 큐+실행기 경유 — 테스트의 가짜 경로는 디스크에 없으므로 충돌 프롬프트는 절대 호출되지 않는다.
        _queue = new OperationQueue(
            new QueuedOperationExecutor(
                _operations, Substitute.For<IConflictPrompt>(), new UndoService(),
                NullLogger<QueuedOperationExecutor>.Instance),
            NullLogger<OperationQueue>.Instance);
    }

    private FileListViewModel CreateViewModel() => new(
        _enumerator, Substitute.For<IFileLauncher>(), _settings, Substitute.For<IShellIconProvider>(),
        _operations, _clipboard, _watcher, Substitute.For<Explorer.Core.Favorites.IFavoritesService>(),
        _queue, NullLogger<FileListViewModel>.Instance);

    private static FileEntry File(string name) => FileEntry.Create(
        @"C:\test\" + name, name, isDirectory: false, 10,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), FileAttributes.Normal);

    private static FileEntry Dir(string name) => FileEntry.Create(
        @"C:\test\" + name, name, isDirectory: true, 0,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), FileAttributes.Directory);

    private async Task<FileListViewModel> CreateLoadedViewModelAsync(params FileEntry[] entries)
    {
        _enumerator.ListAsync(@"C:\test", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>(entries));
        var vm = CreateViewModel();
        await vm.NavigateToAsync(@"C:\test");
        return vm;
    }

    private static void Select(FileListViewModel vm, params string[] names)
    {
        vm.SelectedItems = vm.Items.Where(i => names.Contains(i.Name)).ToArray();
        vm.SelectedItem = vm.SelectedItems.Count > 0 ? vm.SelectedItems[0] : null;
    }

    private int ListCallCount() =>
        _enumerator.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IFileSystemEnumerator.ListAsync));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("시간 내에 조건이 충족되어야 함");
    }

    [Fact]
    public async Task CopySelection_PutsPathsOnClipboardAsCopy()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"), File("b.txt"));
        Select(vm, "a.txt", "b.txt");

        vm.CopySelectionCommand.Execute(null);

        _clipboard.Received(1).SetFiles(
            Arg.Is<IReadOnlyList<string>>(p => p.Count == 2),
            cut: false);
    }

    [Fact]
    public async Task CutSelection_PutsPathsOnClipboardAsCut_AndMarksItems()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"), File("b.txt"));
        Select(vm, "a.txt");

        vm.CutSelectionCommand.Execute(null);

        _clipboard.Received(1).SetFiles(Arg.Any<IReadOnlyList<string>>(), cut: true);
        vm.Items.Single(i => i.Name == "a.txt").IsCut.Should().BeTrue();
        vm.Items.Single(i => i.Name == "b.txt").IsCut.Should().BeFalse();
    }

    [Fact]
    public async Task Paste_Copy_CopiesIntoCurrentFolder_AndReloads()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        _clipboard.GetFiles().Returns(new FileClipboardContent([@"D:\src\x.txt"], IsCut: false));
        var callsBefore = ListCallCount();

        await vm.PasteCommand.ExecuteAsync(null);

        await _operations.Received(1).CopyAsync(
            Arg.Is<IReadOnlyList<string>>(p => p[0] == @"D:\src\x.txt"), @"C:\test", Arg.Any<FileOperationContext?>());
        ListCallCount().Should().Be(callsBefore + 1, "붙여넣기 후 새로고침");
    }

    [Fact]
    public async Task Paste_Cut_MovesAndClearsClipboard()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        _clipboard.GetFiles().Returns(new FileClipboardContent([@"D:\src\x.txt"], IsCut: true));

        await vm.PasteCommand.ExecuteAsync(null);

        await _operations.Received(1).MoveAsync(Arg.Any<IReadOnlyList<string>>(), @"C:\test", Arg.Any<FileOperationContext?>());
        _clipboard.Received(1).Clear();
    }

    [Fact]
    public async Task Delete_RemovesItemsWithoutFullReload()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"), File("b.txt"));
        Select(vm, "a.txt");
        var callsBefore = ListCallCount();

        await vm.DeleteSelectionCommand.ExecuteAsync(null);

        await _operations.Received(1).DeleteAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == @"C:\test\a.txt"), false, Arg.Any<FileOperationContext?>());
        vm.Items.Select(i => i.Name).Should().Equal("b.txt");
        ListCallCount().Should().Be(callsBefore, "삭제는 타겟 업데이트만 하고 재나열하지 않는다");
    }

    [Fact]
    public async Task DeletePermanent_PassesPermanentFlag()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        Select(vm, "a.txt");

        await vm.DeleteSelectionPermanentCommand.ExecuteAsync(null);

        await _operations.Received(1).DeleteAsync(Arg.Any<IReadOnlyList<string>>(), true, Arg.Any<FileOperationContext?>());
    }

    [Fact]
    public async Task Delete_Failure_KeepsItems_AndSetsStatusMessage()
    {
        _operations.DeleteAsync(default!, default)
            .ReturnsForAnyArgs(FileOperationResult.Failure(FileOperationError.AccessDenied));
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        Select(vm, "a.txt");

        await vm.DeleteSelectionCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(1);
        vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CommitRename_Valid_RenamesAndReplacesItem()
    {
        var vm = await CreateLoadedViewModelAsync(File("old.txt"), File("other.txt"));
        Select(vm, "old.txt");
        vm.BeginRenameCommand.Execute(null);
        var item = vm.Items.Single(i => i.Name == "old.txt");
        item.IsRenaming.Should().BeTrue();
        item.EditName = "new.txt";

        await vm.CommitRenameCommand.ExecuteAsync(item);

        await _operations.Received(1).RenameAsync(@"C:\test\old.txt", "new.txt");
        vm.Items.Select(i => i.Name).Should().Contain("new.txt").And.NotContain("old.txt");
        vm.SelectedItem!.Name.Should().Be("new.txt");
    }

    [Fact]
    public async Task CommitRename_InvalidName_DoesNotCallService()
    {
        var vm = await CreateLoadedViewModelAsync(File("old.txt"));
        Select(vm, "old.txt");
        vm.BeginRenameCommand.Execute(null);
        var item = vm.Items.Single();
        item.EditName = "bad|name";

        await vm.CommitRenameCommand.ExecuteAsync(item);

        await _operations.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!);
        vm.StatusMessage.Should().NotBeNullOrEmpty();
        item.IsRenaming.Should().BeFalse();
    }

    [Fact]
    public async Task CommitRename_DuplicateName_DoesNotCallService()
    {
        var vm = await CreateLoadedViewModelAsync(File("old.txt"), File("taken.txt"));
        Select(vm, "old.txt");
        vm.BeginRenameCommand.Execute(null);
        var item = vm.Items.Single(i => i.Name == "old.txt");
        item.EditName = "TAKEN.txt";

        await vm.CommitRenameCommand.ExecuteAsync(item);

        await _operations.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!);
        vm.StatusMessage.Should().Contain("이미");
    }

    [Fact]
    public async Task CreateFolder_GeneratesUniqueName_InsertsSorted_AndBeginsRename()
    {
        var vm = await CreateLoadedViewModelAsync(Dir("새 폴더"), File("z.txt"));

        await vm.CreateFolderCommand.ExecuteAsync(null);

        await _operations.Received(1).CreateFolderAsync(@"C:\test", "새 폴더 (2)");
        var created = vm.Items.Single(i => i.Name == "새 폴더 (2)");
        created.IsDirectory.Should().BeTrue();
        created.IsRenaming.Should().BeTrue();
        vm.SelectedItem.Should().BeSameAs(created);
        vm.Items.Select(i => i.Name).Should().Equal("새 폴더", "새 폴더 (2)", "z.txt");
    }

    [Fact]
    public async Task HandleDrop_IntoOwnSubtree_IsRejected()
    {
        var vm = await CreateLoadedViewModelAsync(Dir("sub"));

        await vm.HandleDropAsync([@"C:\test"], @"C:\test\sub", DropOperation.Move);

        await _operations.DidNotReceiveWithAnyArgs().MoveAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleDrop_ValidMove_MovesAndReloads()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        var callsBefore = ListCallCount();

        await vm.HandleDropAsync([@"D:\src\x.txt"], @"C:\test", DropOperation.Move);

        await _operations.Received(1).MoveAsync(
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == @"D:\src\x.txt"), @"C:\test", Arg.Any<FileOperationContext?>());
        ListCallCount().Should().Be(callsBefore + 1);
    }

    [Fact]
    public async Task WatcherEvent_TriggersReload()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"));
        var callsBefore = ListCallCount();

        _watcher.ChangesDetected += Raise.Event();

        await WaitUntilAsync(() => ListCallCount() == callsBefore + 1);
    }

    [Fact]
    public async Task WatcherEvent_RightAfterOwnOperation_IsSuppressed()
    {
        var vm = await CreateLoadedViewModelAsync(File("a.txt"), File("b.txt"));
        Select(vm, "a.txt");
        await vm.DeleteSelectionCommand.ExecuteAsync(null);
        var callsBefore = ListCallCount();

        _watcher.ChangesDetected += Raise.Event();
        await Task.Delay(200);

        ListCallCount().Should().Be(callsBefore, "직후 에코 이벤트는 억제되어야 함");
    }
}
