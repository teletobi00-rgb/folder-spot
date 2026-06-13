using Explorer.Preview;
using Explorer.Preview.Renderers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.Preview.Tests;

public sealed class PreviewRendererRegistryTests : IDisposable
{
    private readonly TempFiles _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PreviewRendererRegistry Create(params IPreviewRenderer[] renderers) =>
        new(renderers, NullLogger<PreviewRendererRegistry>.Instance);

    [Fact]
    public async Task Render_PicksFirstMatchingRenderer()
    {
        var path = _temp.WriteText("a.cs", "code");
        var registry = Create(
            new TextPreviewRenderer(NullLogger<TextPreviewRenderer>.Instance),
            new InfoPreviewRenderer());

        var result = await registry.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Text);
    }

    [Fact]
    public async Task Render_FallsThroughToInfoRenderer()
    {
        var path = _temp.WriteText("a.unknownext", "data");
        var registry = Create(
            new TextPreviewRenderer(NullLogger<TextPreviewRenderer>.Instance),
            new InfoPreviewRenderer());

        var result = await registry.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Info);
    }

    [Fact]
    public async Task Render_MissingFile_ReturnsNone()
    {
        var registry = Create(new InfoPreviewRenderer());

        var result = await registry.RenderAsync(_temp.Combine("nope.txt"), CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.None);
    }

    [Fact]
    public async Task Render_FolderPath_ReturnsNone()
    {
        var registry = Create(new InfoPreviewRenderer());

        var result = await registry.RenderAsync(_temp.Root, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.None);
    }

    [Fact]
    public async Task Render_RendererThrows_FallsThroughToNext()
    {
        var path = _temp.WriteText("a.txt", "x");
        var throwing = Substitute.For<IPreviewRenderer>();
        throwing.CanRender(Arg.Any<string>()).Returns(true);
        throwing.RenderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<PreviewResult>>(_ => throw new InvalidOperationException("boom"));
        var registry = Create(throwing, new InfoPreviewRenderer());

        var result = await registry.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Info, "예외 렌더러는 건너뛰고 다음으로");
    }

    [Fact]
    public async Task Render_NoMatchAndNoFallback_ReturnsError()
    {
        var path = _temp.WriteText("a.xyz", "x");
        var registry = Create(new TextPreviewRenderer(NullLogger<TextPreviewRenderer>.Instance));

        var result = await registry.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Error);
    }
}
