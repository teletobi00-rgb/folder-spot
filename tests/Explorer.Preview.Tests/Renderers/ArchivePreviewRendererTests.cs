using System.IO;
using System.IO.Compression;
using Explorer.Preview;
using Explorer.Preview.Renderers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Preview.Tests.Renderers;

public sealed class ArchivePreviewRendererTests : IDisposable
{
    private readonly TempFiles _temp = new();
    private readonly ArchivePreviewRenderer _renderer = new(NullLogger<ArchivePreviewRenderer>.Instance);

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("zip", true)]
    [InlineData("7z", true)]
    [InlineData("rar", true)]
    [InlineData("tar", true)]
    [InlineData("txt", false)]
    public void CanRender_MatchesArchiveExtensions(string ext, bool expected)
    {
        _renderer.CanRender(ext).Should().Be(expected);
    }

    [Fact]
    public async Task Render_RealZip_ListsEntries()
    {
        var zipPath = _temp.Combine("test.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            CreateEntry(zip, "readme.txt", "hello");
            CreateEntry(zip, "src/main.cs", "class A {}");
            CreateEntry(zip, "한글/문서.txt", "데이터");
        }

        var result = await _renderer.RenderAsync(zipPath, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Archive);
        result.ArchiveEntries.Should().HaveCount(3);
        result.ArchiveEntries.Select(e => e.Path).Should()
            .Contain("readme.txt").And.Contain("한글/문서.txt");
        result.ArchiveTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task Render_CorruptArchive_ReturnsError()
    {
        var fakeZip = _temp.WriteBytes("broken.zip", [0x00, 0x01, 0x02, 0x03]);

        var result = await _renderer.RenderAsync(fakeZip, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Error);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    private static void CreateEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
