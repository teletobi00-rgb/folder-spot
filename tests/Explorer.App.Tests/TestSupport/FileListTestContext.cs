using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using Explorer.Core.Settings;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.TestSupport;

/// <summary>
/// FileListViewModel 조립용 공용 픽스처 — 열거기/설정/작업/즐겨찾기는 두 페인이 공유하는 mock,
/// 와처/클립보드 등은 인스턴스별 mock.
/// </summary>
internal sealed class FileListTestContext
{
    public IFileSystemEnumerator Enumerator { get; } = Substitute.For<IFileSystemEnumerator>();

    public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();

    public IFileOperationService Operations { get; } = Substitute.For<IFileOperationService>();

    public IFavoritesService Favorites { get; } = Substitute.For<IFavoritesService>();

    public FileListTestContext()
    {
        Settings.Current.Returns(new AppSettings());
        Enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>([]));

        var success = Task.FromResult(FileOperationResult.Success());
        Operations.CopyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(success);
        Operations.MoveAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(success);
        Operations.DeleteAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<bool>()).Returns(success);
        Operations.RenameAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(success);
        Operations.CreateFolderAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(success);
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
