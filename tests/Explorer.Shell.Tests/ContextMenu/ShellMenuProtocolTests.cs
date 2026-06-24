using Explorer.Shell.ContextMenu;
using FluentAssertions;

namespace Explorer.Shell.Tests.ContextMenu;

public sealed class ShellMenuProtocolTests
{
    [Fact]
    public void RoundTrip_PreservesCoordinatesAndPaths()
    {
        using var sw = new StringWriter();
        string[] paths = [@"C:\with space\file.txt", @"\\server\share\문서.docx", @"D:\a"];
        ShellMenuProtocol.WriteRequest(sw, 1280, -47, paths);

        var request = ShellMenuProtocol.ReadRequest(new StringReader(sw.ToString()));

        request.Should().NotBeNull();
        request!.Value.X.Should().Be(1280);
        request.Value.Y.Should().Be(-47);
        request.Value.Paths.Should().Equal(paths);
    }

    [Fact]
    public void RoundTrip_EmptyPathList_IsPreserved()
    {
        using var sw = new StringWriter();
        ShellMenuProtocol.WriteRequest(sw, 0, 0, []);

        var request = ShellMenuProtocol.ReadRequest(new StringReader(sw.ToString()));

        request.Should().NotBeNull();
        request!.Value.Paths.Should().BeEmpty();
    }

    [Fact]
    public void ReadRequest_AtEndOfStream_ReturnsNull()
    {
        ShellMenuProtocol.ReadRequest(new StringReader(string.Empty)).Should().BeNull();
    }

    [Fact]
    public void ReadRequest_TruncatedBody_ReturnsNull()
    {
        // 헤더는 3개를 약속했지만 본문이 1줄뿐 — 잘린 요청은 null(연결 종료로 간주).
        ShellMenuProtocol.ReadRequest(new StringReader("10 20 3\nonly-one\n")).Should().BeNull();
    }

    [Fact]
    public void ReadRequest_MalformedHeader_ReturnsNull()
    {
        ShellMenuProtocol.ReadRequest(new StringReader("not a header\nx\n")).Should().BeNull();
    }

    [Fact]
    public void ReadRequest_NegativeCount_ReturnsNull()
    {
        ShellMenuProtocol.ReadRequest(new StringReader("10 20 -1\n")).Should().BeNull();
    }

    [Fact]
    public void ReadRequest_AbsurdCount_ReturnsNullWithoutAllocating()
    {
        // 손상된 헤더가 거대한 count를 줘도 과도한 할당 없이 null.
        ShellMenuProtocol.ReadRequest(new StringReader("10 20 2000000000\n")).Should().BeNull();
    }
}
