using Explorer.Core.FileSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Core.Tests.FileSystem;

public sealed class FileSystemFolderWatcherTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemFolderWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Watch_RaisesDebouncedEventOnFileCreation()
    {
        using var watcher = new FileSystemFolderWatcher(
            NullLogger<FileSystemFolderWatcher>.Instance,
            debounce: TimeSpan.FromMilliseconds(100));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.ChangesDetected += (_, _) => signal.TrySetResult();
        watcher.Watch(_tempDir);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "new.txt"), "x");

        var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(signal.Task, "파일 생성이 디바운스 후 이벤트를 발생시켜야 함");
    }

    [Fact]
    public async Task Stop_PreventsFurtherEvents()
    {
        using var watcher = new FileSystemFolderWatcher(
            NullLogger<FileSystemFolderWatcher>.Instance,
            debounce: TimeSpan.FromMilliseconds(50));
        var count = 0;
        watcher.ChangesDetected += (_, _) => Interlocked.Increment(ref count);
        watcher.Watch(_tempDir);
        watcher.StopWatching();

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "after-stop.txt"), "x");
        await Task.Delay(400);

        count.Should().Be(0);
    }

    [Fact]
    public void Watch_NonExistentFolder_DoesNotThrow()
    {
        using var watcher = new FileSystemFolderWatcher(NullLogger<FileSystemFolderWatcher>.Instance);

        var act = () => watcher.Watch(Path.Combine(_tempDir, "missing"));

        act.Should().NotThrow();
    }
}
