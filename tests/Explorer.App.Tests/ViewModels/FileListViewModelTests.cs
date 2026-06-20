using System.IO;
using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using Explorer.Core.Settings;
using Explorer.Core.Sorting;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explorer.App.Tests.ViewModels;

public sealed class FileListViewModelTests
{
    private readonly IFileSystemEnumerator _enumerator = Substitute.For<IFileSystemEnumerator>();
    private readonly IFileLauncher _launcher = Substitute.For<IFileLauncher>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IShellIconProvider _icons = Substitute.For<IShellIconProvider>();
    private readonly IFileOperationService _operations = Substitute.For<IFileOperationService>();
    private readonly IFileClipboardService _clipboard = Substitute.For<IFileClipboardService>();
    private readonly IFolderWatcher _watcher = Substitute.For<IFolderWatcher>();
    private readonly IFavoritesService _favorites = Substitute.For<IFavoritesService>();
    private readonly Explorer.Core.Operations.IOperationQueue _queue =
        Substitute.For<Explorer.Core.Operations.IOperationQueue>();

    public FileListViewModelTests()
    {
        _settings.Current.Returns(new AppSettings());
        TestSupport.StreamingEnumeratorStub.StreamFromList(_enumerator);
    }

    private FileListViewModel CreateViewModel() => new(
        _enumerator, _launcher, _settings, _icons, _operations, _clipboard, _watcher, _favorites, _queue,
        NullLogger<FileListViewModel>.Instance);

    private static FileEntry File(string name, bool hidden = false) => FileEntry.Create(
        @"C:\test\" + name, name, isDirectory: false, 10,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 1),
        hidden ? FileAttributes.Hidden : FileAttributes.Normal);

    private static FileEntry Dir(string name) => FileEntry.Create(
        @"C:\test\" + name, name, isDirectory: true, 0,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), FileAttributes.Directory);

    private void SetupListing(string path, params FileEntry[] entries) =>
        _enumerator.ListAsync(path, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>(entries));

    [Fact]
    public async Task NavigateTo_LoadsItems_SortedFoldersFirst()
    {
        SetupListing(@"C:\test", File("b.txt"), Dir("zdir"), File("a.txt"), Dir("adir"));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test");

        vm.CurrentPath.Should().Be(@"C:\test");
        vm.Items.Select(i => i.Name).Should().Equal("adir", "zdir", "a.txt", "b.txt");
        vm.Status.Should().Be(FileListStatus.None);
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task NavigateTo_HiddenFilesFilteredByDefault()
    {
        SetupListing(@"C:\test", File("visible.txt"), File("hidden.txt", hidden: true));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test");

        vm.Items.Select(i => i.Name).Should().Equal("visible.txt");
    }

    [Fact]
    public async Task NavigateTo_ShowsHidden_WhenSettingEnabled()
    {
        _settings.Current.Returns(new AppSettings { ShowHiddenFiles = true });
        SetupListing(@"C:\test", File("visible.txt"), File("hidden.txt", hidden: true));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test");

        vm.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task NavigateTo_EmptyDirectory_SetsEmptyStatus()
    {
        SetupListing(@"C:\test");
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test");

        vm.Status.Should().Be(FileListStatus.Empty);
    }

    [Fact]
    public async Task NavigateTo_MissingDirectory_SetsNotFound()
    {
        _enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DirectoryNotFoundException());
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\missing");

        vm.Status.Should().Be(FileListStatus.NotFound);
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task NavigateTo_AccessDenied_SetsAccessDenied()
    {
        _enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException());
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\protected");

        vm.Status.Should().Be(FileListStatus.AccessDenied);
    }

    [Fact]
    public async Task NavigateTo_InvalidPath_SetsErrorWithoutTouchingHistory()
    {
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"relative\path");

        vm.Status.Should().Be(FileListStatus.Error);
        vm.History.Current.Should().BeNull();
    }

    [Fact]
    public async Task BackAndForward_NavigateThroughHistory()
    {
        SetupListing(@"C:\a", File("a.txt"));
        SetupListing(@"C:\b", File("b.txt"));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\a");
        vm.GoBackCommand.CanExecute(null).Should().BeFalse();

        await vm.NavigateToAsync(@"C:\b");
        vm.GoBackCommand.CanExecute(null).Should().BeTrue();

        await vm.GoBackCommand.ExecuteAsync(null);
        vm.CurrentPath.Should().Be(@"C:\a");
        vm.Items.Single().Name.Should().Be("a.txt");
        vm.GoForwardCommand.CanExecute(null).Should().BeTrue();

        await vm.GoForwardCommand.ExecuteAsync(null);
        vm.CurrentPath.Should().Be(@"C:\b");
    }

    [Fact]
    public async Task GoUp_NavigatesToParent_DisabledAtRoot()
    {
        SetupListing(@"C:\test\sub", File("x.txt"));
        SetupListing(@"C:\test", File("y.txt"));
        SetupListing(@"C:\", File("z.txt"));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test\sub");
        await vm.GoUpCommand.ExecuteAsync(null);
        vm.CurrentPath.Should().Be(@"C:\test");

        await vm.NavigateToAsync(@"C:\");
        vm.GoUpCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task OpenItem_Directory_NavigatesIntoIt()
    {
        SetupListing(@"C:\test", Dir("sub"));
        SetupListing(@"C:\test\sub", File("inner.txt"));
        var vm = CreateViewModel();
        await vm.NavigateToAsync(@"C:\test");

        await vm.OpenItemCommand.ExecuteAsync(vm.Items.Single());

        vm.CurrentPath.Should().Be(@"C:\test\sub");
    }

    [Fact]
    public async Task OpenItem_File_LaunchesIt()
    {
        SetupListing(@"C:\test", File("doc.txt"));
        var vm = CreateViewModel();
        await vm.NavigateToAsync(@"C:\test");

        await vm.OpenItemCommand.ExecuteAsync(vm.Items.Single());

        _launcher.Received(1).Launch(@"C:\test\doc.txt");
    }

    [Fact]
    public async Task OpenItem_LaunchFailure_SetsStatusMessageInsteadOfThrowing()
    {
        SetupListing(@"C:\test", File("broken.xyz"));
        _launcher.When(l => l.Launch(Arg.Any<string>())).Throw(new InvalidOperationException("연결 안 됨"));
        var vm = CreateViewModel();
        await vm.NavigateToAsync(@"C:\test");

        await vm.OpenItemCommand.ExecuteAsync(vm.Items.Single());

        vm.StatusMessage.Should().Contain("연결 안 됨");
    }

    [Fact]
    public async Task ChangeSort_TogglesDirection_AndReusesItemInstances()
    {
        SetupListing(@"C:\test", File("a.txt"), File("b.txt"));
        var vm = CreateViewModel();
        await vm.NavigateToAsync(@"C:\test");
        var originalItems = vm.Items.ToHashSet();

        await vm.ChangeSortCommand.ExecuteAsync(SortColumn.Name);

        vm.Sort.Should().Be(new SortDescriptor(SortColumn.Name, Descending: true));
        vm.Items.Select(i => i.Name).Should().Equal("b.txt", "a.txt");
        vm.Items.ToHashSet().SetEquals(originalItems).Should().BeTrue("재정렬은 항목 인스턴스를 재사용해야 함");
    }

    [Fact]
    public async Task RapidNavigation_DiscardsStaleResult()
    {
        var slowFirst = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        _enumerator.ListAsync(@"C:\a", Arg.Any<CancellationToken>()).Returns(slowFirst.Task);
        SetupListing(@"C:\b", File("b.txt"));
        var vm = CreateViewModel();

        var firstNavigation = vm.NavigateToAsync(@"C:\a");
        await vm.NavigateToAsync(@"C:\b");
        slowFirst.SetResult([File("a-stale.txt")]);
        await firstNavigation;

        vm.CurrentPath.Should().Be(@"C:\b");
        vm.Items.Select(i => i.Name).Should().Equal("b.txt");
    }

    [Fact]
    public async Task NavigateTo_StreamsMultipleBatches_AccumulatesAndSortsAll()
    {
        // 스트리밍이 여러 배치로 나눠 와도 누적·정렬되어 전체가 폴더 우선·이름순으로 표시되어야 한다.
        _enumerator.StreamAsync(@"C:\test", Arg.Any<CancellationToken>())
            .Returns(_ => Batches(
                [File("c.txt"), File("a.txt")],
                [Dir("zdir"), File("b.txt")]));
        var vm = CreateViewModel();

        await vm.NavigateToAsync(@"C:\test");

        vm.Items.Select(i => i.Name).Should().Equal("zdir", "a.txt", "b.txt", "c.txt");
    }

    private static async IAsyncEnumerable<IReadOnlyList<FileEntry>> Batches(params FileEntry[][] batches)
    {
        foreach (var batch in batches)
        {
            await Task.Yield();
            yield return batch;
        }
    }
}
