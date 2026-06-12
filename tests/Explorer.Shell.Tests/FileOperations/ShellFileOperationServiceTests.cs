using Explorer.Core.FileOperations;
using Explorer.Shell.FileOperations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Shell.Tests.FileOperations;

/// <summary>실제 IFileOperation을 temp 디렉터리에서 구동하는 통합 테스트 (silent 모드, 휴지통 미사용).</summary>
public sealed class ShellFileOperationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ShellFileOperationService _service;

    public ShellFileOperationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ShellFileOperationService(
            () => 0,
            NullLogger<ShellFileOperationService>.Instance,
            silent: true);
    }

    public void Dispose()
    {
        _service.Dispose();
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

    private string CreateFile(string name, string content = "test")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task CopyAsync_CopiesFileIntoDestination()
    {
        var source = CreateFile("원본.txt", "내용");
        var dest = CreateDir("대상");

        var result = await _service.CopyAsync([source], dest);

        result.Succeeded.Should().BeTrue(result.Message);
        File.Exists(Path.Combine(dest, "원본.txt")).Should().BeTrue();
        File.Exists(source).Should().BeTrue("복사는 원본을 남긴다");
    }

    [Fact]
    public async Task MoveAsync_MovesFileIntoDestination()
    {
        var source = CreateFile("이동할파일.txt");
        var dest = CreateDir("대상");

        var result = await _service.MoveAsync([source], dest);

        result.Succeeded.Should().BeTrue(result.Message);
        File.Exists(Path.Combine(dest, "이동할파일.txt")).Should().BeTrue();
        File.Exists(source).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_Permanent_RemovesFile()
    {
        var target = CreateFile("삭제대상.txt");

        var result = await _service.DeleteAsync([target], permanent: true);

        result.Succeeded.Should().BeTrue(result.Message);
        File.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_RenamesFile()
    {
        var source = CreateFile("이전이름.txt", "데이터");

        var result = await _service.RenameAsync(source, "새이름.txt");

        result.Succeeded.Should().BeTrue(result.Message);
        File.Exists(Path.Combine(_tempDir, "새이름.txt")).Should().BeTrue();
        File.Exists(source).Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_InvalidName_FailsWithoutTouchingShell()
    {
        var source = CreateFile("유지.txt");

        var result = await _service.RenameAsync(source, "bad|name");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(FileOperationError.InvalidName);
        File.Exists(source).Should().BeTrue();
    }

    [Fact]
    public async Task CreateFolderAsync_CreatesDirectory()
    {
        var result = await _service.CreateFolderAsync(_tempDir, "새 폴더");

        result.Succeeded.Should().BeTrue(result.Message);
        Directory.Exists(Path.Combine(_tempDir, "새 폴더")).Should().BeTrue();
    }

    [Fact]
    public async Task CopyAsync_MissingSource_ReportsFailure()
    {
        var dest = CreateDir("대상");

        var result = await _service.CopyAsync([Path.Combine(_tempDir, "없는파일.txt")], dest);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBe(FileOperationError.None);
    }

    [Fact]
    public async Task CopyAsync_EmptySources_ReportsFailure()
    {
        var result = await _service.CopyAsync([], _tempDir);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task MoveAsync_Directory_MovesWholeTree()
    {
        var sourceDir = CreateDir("폴더트리");
        File.WriteAllText(Path.Combine(sourceDir, "안쪽.txt"), "x");
        var dest = CreateDir("대상");

        var result = await _service.MoveAsync([sourceDir], dest);

        result.Succeeded.Should().BeTrue(result.Message);
        File.Exists(Path.Combine(dest, "폴더트리", "안쪽.txt")).Should().BeTrue();
        Directory.Exists(sourceDir).Should().BeFalse();
    }
}
