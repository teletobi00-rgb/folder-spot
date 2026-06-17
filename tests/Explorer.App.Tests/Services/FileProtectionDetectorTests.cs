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

    private static byte[] Zip() => [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0];

    private static byte[] Pdf(string body) => Encoding.ASCII.GetBytes("%PDF-2.0\n" + body);

    /// <summary>최소 유효 CFBF(헤더 + 디렉터리 섹터0 + FAT 섹터1)를 만들어 주어진 디렉터리 엔트리를 넣는다.</summary>
    private static byte[] BuildCfbf(params (string Name, byte Type)[] entries)
    {
        const int sectorSize = 512;
        var buf = new byte[sectorSize * 3];

        byte[] sig = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        Array.Copy(sig, buf, 8);
        BitConverter.GetBytes((ushort)3).CopyTo(buf, 0x1A);       // major version
        BitConverter.GetBytes((ushort)0xFFFE).CopyTo(buf, 0x1C);  // byte order LE
        BitConverter.GetBytes((ushort)9).CopyTo(buf, 0x1E);       // sector shift → 512B
        BitConverter.GetBytes((ushort)6).CopyTo(buf, 0x20);       // mini sector shift
        BitConverter.GetBytes(1u).CopyTo(buf, 0x2C);              // num FAT sectors
        BitConverter.GetBytes(0u).CopyTo(buf, 0x30);              // first dir sector = 0
        for (var i = 0; i < 109; i++)
        {
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(buf, 0x4C + (i * 4));
        }

        BitConverter.GetBytes(1u).CopyTo(buf, 0x4C);              // DIFAT[0] = FAT at sector 1

        var dirBase = sectorSize;
        for (var e = 0; e < entries.Length && e < 4; e++)
        {
            var eo = dirBase + (e * 128);
            var nameBytes = Encoding.Unicode.GetBytes(entries[e].Name);
            Array.Copy(nameBytes, 0, buf, eo, nameBytes.Length);
            BitConverter.GetBytes((ushort)(nameBytes.Length + 2)).CopyTo(buf, eo + 0x40); // nameLen incl null
            buf[eo + 0x42] = entries[e].Type;                                              // object type
        }

        var fatBase = sectorSize * 2;
        BitConverter.GetBytes(0xFFFFFFFEu).CopyTo(buf, fatBase);       // sector 0 (dir) = ENDOFCHAIN
        BitConverter.GetBytes(0xFFFFFFFDu).CopyTo(buf, fatBase + 4);   // sector 1 (fat) = FATSECT
        for (var j = 2; j < sectorSize / 4; j++)
        {
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(buf, fatBase + (j * 4));
        }

        return buf;
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("docx", true)]
    [InlineData("doc", true)]
    [InlineData("ppt", true)]
    [InlineData("xls", true)]
    [InlineData("txt", false)]
    [InlineData("", false)]
    public void MaybeProtectable_GatesByExtension(string ext, bool expected) =>
        FileProtectionDetector.MaybeProtectable(ext).Should().Be(expected);

    [Fact]
    public void Cfbf_WithEncryptedPackage_IsProtected()
    {
        var path = Write("secret.docx", BuildCfbf(("Root Entry", 5), ("EncryptedPackage", 2)));
        FileProtectionDetector.IsProtected(path, "docx").Should().BeTrue();
    }

    [Fact]
    public void Cfbf_WithDataSpacesStorage_IsProtected()
    {
        // DataSpaces 스토리지 이름은 제어문자 U+0006 접두 — 트리밍 후 매칭되어야 한다.
        var path = Write("aip.docx", BuildCfbf(("Root Entry", 5), (new string(new[]{(char)6,(char)68,(char)97,(char)116,(char)97,(char)83,(char)112,(char)97,(char)99,(char)101,(char)115}), 1)));
        FileProtectionDetector.IsProtected(path, "docx").Should().BeTrue();
    }

    [Fact]
    public void Cfbf_NormalLegacyDoc_IsNotProtected()
    {
        // 일반 레거시 .doc은 항상 CFBF이지만 암호화 스트림이 없다 → 보호 아님(이전 버그: bare CFBF를 보호로 오판).
        var path = Write("plain.doc", BuildCfbf(("Root Entry", 5), ("WordDocument", 2), ("1Table", 2)));
        FileProtectionDetector.IsProtected(path, "doc").Should().BeFalse();
    }

    [Fact]
    public void Zip_Ooxml_IsNotProtected()
    {
        var path = Write("plain.docx", Zip());
        FileProtectionDetector.IsProtected(path, "docx").Should().BeFalse();
    }

    [Fact]
    public void Pdf_AipEncryptedPayload_IsProtected()
    {
        // 실제 케이스: %PDF-2.0 + /EncryptedPayload + /MicrosoftIRMService (앞부분).
        var body = "1 0 obj<</AFRelationship /EncryptedPayload/EP <</Subtype /MicrosoftIRMService>>>>\n"
            + new string('x', 100_000) + "\ntrailer<<>>\n%%EOF";
        var path = Write("irm.pdf", Pdf(body));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeTrue();
    }

    [Fact]
    public void Pdf_PasswordEncryptInTrailer_IsProtected()
    {
        // /Encrypt가 트레일러(끝)에만 있고 256KB head 밖 → tail 스캔으로 잡혀야 한다.
        var body = new string('x', 300_000) + "\ntrailer<</Root 1 0 R/Encrypt 2 0 R>>\n%%EOF";
        var path = Write("locked.pdf", Pdf(body));
        FileProtectionDetector.IsProtected(path, "pdf").Should().BeTrue();
    }

    [Fact]
    public void Pdf_WithoutProtection_IsNotProtected()
    {
        var body = new string('x', 300_000) + "\ntrailer<</Root 1 0 R>>\n%%EOF";
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
