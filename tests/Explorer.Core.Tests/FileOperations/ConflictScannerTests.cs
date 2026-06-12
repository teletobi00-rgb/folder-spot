using Explorer.Core.FileOperations;
using FluentAssertions;

namespace Explorer.Core.Tests.FileOperations;

public sealed class ConflictScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _destDir;

    public ConflictScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempDir, "src");
        _destDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
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

    private string MakeSourceFile(string name, string content = "src")
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Scan_NoCollisions_GivesEmpty()
    {
        var source = MakeSourceFile("unique.txt");

        ConflictScanner.Scan([source], _destDir).Should().BeEmpty();
    }

    [Fact]
    public void Scan_FileCollision_ReportsBothSidesMetadata()
    {
        var source = MakeSourceFile("dup.txt", "12345");
        File.WriteAllText(Path.Combine(_destDir, "dup.txt"), "abc");

        var conflicts = ConflictScanner.Scan([source], _destDir);

        var conflict = conflicts.Should().ContainSingle().Subject;
        conflict.Name.Should().Be("dup.txt");
        conflict.IsDirectory.Should().BeFalse();
        conflict.SourceSize.Should().Be(5);
        conflict.TargetSize.Should().Be(3);
        conflict.TargetPath.Should().Be(Path.Combine(_destDir, "dup.txt"));
    }

    [Fact]
    public void Scan_DirectoryCollision_IsFlaggedAsDirectory()
    {
        var sourceSub = Path.Combine(_sourceDir, "folder");
        Directory.CreateDirectory(sourceSub);
        Directory.CreateDirectory(Path.Combine(_destDir, "folder"));

        var conflicts = ConflictScanner.Scan([sourceSub], _destDir);

        conflicts.Should().ContainSingle().Which.IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void Scan_MixedSources_ReportsOnlyColliding()
    {
        var a = MakeSourceFile("a.txt");
        var b = MakeSourceFile("b.txt");
        File.WriteAllText(Path.Combine(_destDir, "b.txt"), "x");

        var conflicts = ConflictScanner.Scan([a, b], _destDir);

        conflicts.Should().ContainSingle().Which.Name.Should().Be("b.txt");
    }
}
