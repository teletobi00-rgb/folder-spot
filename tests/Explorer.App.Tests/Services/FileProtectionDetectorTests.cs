using System.IO;
using System.Text;
using Explorer.App.Services;
using FluentAssertions;

namespace Explorer.App.Tests.Services;

public sealed class FileProtectionDetectorTests : IDisposable
{
    private readonly string _dir;

    public FileProtectionDetectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Cfbf(int totalSize = 512)
    {
        var buffer = new byte[totalSize];
        byte[] sig = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        Array.Copy(sig, buffer, sig.Length);
        return buffer;
    }

    private static byte[] Zip() => [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0];

    private static byte[] Pdf(string body) => Encoding.ASCII.GetBytes("%PDF-1.7\n" + body);

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("docx", true)]
    [InlineData("xlsx", true)]
    [InlineData("txt", false)]
    [InlineData("", false)]
    public void MaybeProtectable_GatesByExtension(string ext, bool expected) =>
        FileProtectionDetector.MaybeProtectable(ext).Should().Be(expected);

    [Fact]
    public void Cfbf_Ooxml_IsProtected()
    {
        var path = Write("secret.docx", Cfbf());
        FileProtectionDetector.IsProtected(path, "docx").Should().BeTrue();
    }

    [Fact]
    public void Zip_Ooxml_IsNotProtected()
    {
        var path = Write("plain.docx", Zip());
        FileProtectionDetector.IsProtected(path, "docx").Should().BeFalse();
    }

    [Fact]
    public void Pdf_WithEncryptInTrailer_IsProtected()
    {
        // /Encrypt only at the very end (트레일러), past the 1KB head window → must be found by the tail scan.
        var body = new string('x', 200_000) + "\ntrailer<</Root 1 0 R/Encrypt 2 0 R>>\n%%EOF";
        var path = Write("locked.pdf", Pdf(body));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeTrue();
    }

    [Fact]
    public void Pdf_Linearized_EncryptInHead_IsProtected()
    {
        // /Encrypt near the front (선형화 PDF) — found by the head scan even with a huge body before the trailer.
        var body = "<</Linearized 1/Encrypt 3 0 R>>\n" + new string('x', 200_000) + "\ntrailer<<>>\n%%EOF";
        var path = Write("front.pdf", Pdf(body));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeTrue();
    }

    [Fact]
    public void Pdf_WithoutEncrypt_IsNotProtected()
    {
        var body = new string('x', 200_000) + "\ntrailer<</Root 1 0 R>>\n%%EOF";
        var path = Write("open.pdf", Pdf(body));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeFalse();
    }

    [Fact]
    public void NonPdfBytes_WithPdfExtension_IsNotProtected()
    {
        var path = Write("fake.pdf", Encoding.ASCII.GetBytes("not a pdf but mentions /Encrypt here"));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeFalse();
    }

    [Fact]
    public void NonProtectableExtension_IsNotProtected()
    {
        var path = Write("note.txt", Encoding.ASCII.GetBytes("hello"));
        FileProtectionDetector.IsProtected(path, "txt").Should().BeFalse();
    }
}
