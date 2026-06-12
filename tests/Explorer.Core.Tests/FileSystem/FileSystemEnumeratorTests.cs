using Explorer.Core.FileSystem;
using FluentAssertions;

namespace Explorer.Core.Tests.FileSystem;

public sealed class FileSystemEnumeratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemEnumerator _enumerator = new();

    public FileSystemEnumeratorTests()
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
    public async Task ListAsync_ReturnsFilesAndDirectoriesWithMetadata()
    {
        File.WriteAllText(Path.Combine(_tempDir, "한글파일.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));

        var entries = await _enumerator.ListAsync(_tempDir);

        entries.Should().HaveCount(2);

        var file = entries.Single(e => !e.IsDirectory);
        file.Name.Should().Be("한글파일.txt");
        file.Extension.Should().Be("txt");
        file.Size.Should().Be(5);
        file.FullPath.Should().Be(Path.Combine(_tempDir, "한글파일.txt"));
        file.DateModified.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(2));

        var dir = entries.Single(e => e.IsDirectory);
        dir.Name.Should().Be("sub");
        dir.Extension.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_IncludesHiddenEntries()
    {
        var hiddenPath = Path.Combine(_tempDir, "hidden.txt");
        File.WriteAllText(hiddenPath, "h");
        File.SetAttributes(hiddenPath, FileAttributes.Hidden);

        var entries = await _enumerator.ListAsync(_tempDir);

        entries.Should().ContainSingle(e => e.Name == "hidden.txt" && e.IsHidden);
    }

    [Fact]
    public async Task ListAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var entries = await _enumerator.ListAsync(_tempDir);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_MissingDirectory_ThrowsDirectoryNotFound()
    {
        var act = () => _enumerator.ListAsync(Path.Combine(_tempDir, "does-not-exist"));

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task ListAsync_RelativePath_ThrowsArgumentException()
    {
        var act = () => _enumerator.ListAsync(@"relative\path");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ListAsync_AlreadyCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _enumerator.ListAsync(_tempDir, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
