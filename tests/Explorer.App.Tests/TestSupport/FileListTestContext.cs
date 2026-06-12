using Explorer.App.ViewModels;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Settings;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.TestSupport;

/// <summary>FileListViewModel 조립용 공용 픽스처 — 열거기/설정은 공유, 나머지는 인스턴스별 mock.</summary>
internal sealed class FileListTestContext
{
    public IFileSystemEnumerator Enumerator { get; } = Substitute.For<IFileSystemEnumerator>();

    public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();

    public FileListTestContext()
    {
        Settings.Current.Returns(new AppSettings());
        Enumerator.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FileEntry>>([]));
    }

    public FileListViewModel CreateFileList() => new(
        Enumerator,
        Substitute.For<IFileLauncher>(),
        Settings,
        Substitute.For<IShellIconProvider>(),
        Substitute.For<IFileOperationService>(),
        Substitute.For<IFileClipboardService>(),
        Substitute.For<IFolderWatcher>(),
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
