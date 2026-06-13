using Explorer.Preview;
using Explorer.Preview.Renderers;
using FluentAssertions;

namespace Explorer.Preview.Tests.Renderers;

public sealed class InfoPreviewRendererTests : IDisposable
{
    private readonly TempFiles _temp = new();
    private readonly InfoPreviewRenderer _renderer = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void CanRender_AlwaysTrue()
    {
        _renderer.CanRender("anything").Should().BeTrue();
        _renderer.CanRender("").Should().BeTrue();
    }

    [Fact]
    public async Task Render_File_ProducesInfoLines()
    {
        var path = _temp.WriteText("data.bin", "12345");

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Info);
        result.InfoLines.Should().Contain(l => l.Label == "크기" && l.Value.Contains('5'));
        result.InfoLines.Should().Contain(l => l.Label == "종류");
        result.InfoLines.Should().Contain(l => l.Label == "수정한 날짜");
    }

    [Fact]
    public async Task Render_MissingFile_ReturnsError()
    {
        var result = await _renderer.RenderAsync(_temp.Combine("gone.bin"), CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Error);
    }
}
