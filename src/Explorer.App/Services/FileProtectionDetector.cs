using System.IO;

namespace Explorer.App.Services;

/// <summary>
/// 보호(암호화/AIP·IRM) Office 파일 감지. AIP/암호로 보호된 OOXML(docx/xlsx/pptx 등)은 ZIP이 아니라
/// CFBF(OLE 복합 문서, EncryptedPackage)로 저장되므로, OOXML 확장자인데 헤더가 CFBF면 보호된 것이다.
/// (라벨만 있고 암호화 없는 경우나 레거시 doc/xls/ppt·암호 PDF는 v1 범위 밖.)
/// </summary>
public static class FileProtectionDetector
{
    private static readonly HashSet<string> OoxmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "docm", "dotx", "dotm",
        "xlsx", "xlsm", "xlsb", "xltx", "xltm",
        "pptx", "pptm", "ppsx", "ppsm", "potx", "potm",
    };

    private static ReadOnlySpan<byte> CfbfSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>헤더를 읽지 않고 빠르게 — 보호 가능성이 있는 형식(OOXML)인지.</summary>
    public static bool MaybeProtectable(string extensionWithoutDot) =>
        !string.IsNullOrEmpty(extensionWithoutDot) && OoxmlExtensions.Contains(extensionWithoutDot);

    /// <summary>OOXML 파일이 암호화/AIP 보호(=CFBF로 저장)됐는지. 접근 불가/그 외 형식은 false.</summary>
    public static bool IsProtected(string path, string extensionWithoutDot)
    {
        if (!MaybeProtectable(extensionWithoutDot))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            return stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) >= 8
                && header.SequenceEqual(CfbfSignature);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
