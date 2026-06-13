using Explorer.Preview;
using Explorer.Preview.Renderers;
using FluentAssertions;

namespace Explorer.Preview.Tests.Renderers;

public sealed class SimpleRendererTests : IDisposable
{
    private readonly TempFiles _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("png", true)]
    [InlineData("jpg", true)]
    [InlineData("webp", true)]
    [InlineData("txt", false)]
    [InlineData("mp4", false)]
    public void Image_CanRender(string ext, bool expected) =>
        new ImagePreviewRenderer().CanRender(ext).Should().Be(expected);

    [Theory]
    [InlineData("mp4", true)]
    [InlineData("mp3", true)]
    [InlineData("flac", true)]
    [InlineData("png", false)]
    public void Media_CanRender(string ext, bool expected) =>
        new MediaPreviewRenderer().CanRender(ext).Should().Be(expected);

    [Fact]
    public async Task Image_Render_ReturnsImageKindWithPath()
    {
        var path = _temp.WriteBytes("pic.png", [1, 2, 3]);

        var result = await new ImagePreviewRenderer().RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Image);
        result.FilePath.Should().Be(path);
        result.DisplayName.Should().Be("pic.png");
    }

    [Fact]
    public async Task Media_Render_ReturnsMediaKind()
    {
        var path = _temp.WriteBytes("clip.mp4", [1, 2, 3]);

        var result = await new MediaPreviewRenderer().RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Media);
    }
}
