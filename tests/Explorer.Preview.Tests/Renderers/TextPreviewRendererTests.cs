using System.Text;
using Explorer.Preview;
using Explorer.Preview.Renderers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Preview.Tests.Renderers;

public sealed class TextPreviewRendererTests : IDisposable
{
    private readonly TempFiles _temp = new();
    private readonly TextPreviewRenderer _renderer = new(NullLogger<TextPreviewRenderer>.Instance);

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("cs", true)]
    [InlineData("json", true)]
    [InlineData("txt", true)]
    [InlineData("yml", true)]
    [InlineData("png", false)]
    [InlineData("zip", false)]
    public void CanRender_MatchesTextExtensions(string ext, bool expected)
    {
        _renderer.CanRender(ext).Should().Be(expected);
    }

    [Fact]
    public async Task Render_Utf8File_ReturnsContentAndLanguageHint()
    {
        var path = _temp.WriteText("a.cs", "class Foo { }");

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Text);
        result.Text.Should().Be("class Foo { }");
        result.LanguageHint.Should().Be("C#");
        result.EncodingName.Should().Be("UTF-8");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Render_KoreanContent_RoundtripsCorrectly()
    {
        var path = _temp.WriteText("메모.txt", "안녕하세요\n회의록입니다.");

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.Text.Should().Contain("안녕하세요").And.Contain("회의록");
        result.LanguageHint.Should().BeNull("일반 텍스트는 언어 힌트 없음");
    }

    [Fact]
    public async Task Render_Utf8BomFile_DetectsBomEncoding()
    {
        var path = _temp.WriteText("bom.txt", "héllo", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.EncodingName.Should().Be("UTF-8 BOM");
        result.Text.Should().Be("héllo");
    }

    [Fact]
    public async Task Render_Utf16File_DetectsEncoding()
    {
        var path = _temp.WriteText("utf16.txt", "데이터", Encoding.Unicode);

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.EncodingName.Should().Be("UTF-16 LE");
        result.Text.Should().Be("데이터");
    }

    [Fact]
    public async Task Render_InvalidUtf8_FallsBackToLatin1()
    {
        // 0xFF는 유효한 UTF-8 시작 바이트가 아니다 → Latin-1 폴백
        var path = _temp.WriteBytes("bin.log", [0x48, 0x69, 0xFF, 0xFE, 0x21]);

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.EncodingName.Should().Be("Latin-1");
        result.Text.Should().StartWith("Hi");
    }

    [Fact]
    public async Task Render_LargeFile_IsTruncated()
    {
        var path = _temp.WriteText("big.txt", new string('a', 2 * 1024 * 1024));

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.Truncated.Should().BeTrue();
        result.Text!.Length.Should().BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public async Task Render_Utf8MultibyteSplitAtSizeCap_StaysUtf8NotLatin1()
    {
        // 1MB 경계가 한글(3바이트) 시퀀스 중간에 떨어지도록 구성 — 꼬리 트림이 없으면 전체가 Latin-1로 오판된다.
        var path = _temp.WriteText("boundary.txt", new string('a', 1024 * 1024 - 1) + "가");

        var result = await _renderer.RenderAsync(path, CancellationToken.None);

        result.EncodingName.Should().Be("UTF-8", "잘린 꼬리만 제외하고 UTF-8로 유지");
        result.Truncated.Should().BeTrue();
        result.Text.Should().NotContain("Ã", "Latin-1 mojibake가 아니어야 함");
    }

    [Fact]
    public async Task Render_MissingFile_ReturnsError()
    {
        var result = await _renderer.RenderAsync(_temp.Combine("nope.txt"), CancellationToken.None);

        result.Kind.Should().Be(PreviewKind.Error);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
