using System.Globalization;
using System.IO;
using Explorer.Core.Caching;

namespace Explorer.App.Services;

/// <summary>
/// 보호(암호화/AIP·IRM·권한 제한) 파일 감지. <b>실제로 암호화/IRM 보호된 파일만</b> 표시한다(라벨만 붙은 건 제외).
/// <list type="bullet">
/// <item>Office(OOXML·레거시 doc/xls/ppt): 암호화/AIP면 CFBF(OLE 복합 문서) 안에 EncryptedPackage·DataSpaces·
/// EncryptionInfo 스트림이 생긴다. 일반 OOXML은 ZIP, 일반 레거시는 그 스트림이 없으므로 구분된다.</item>
/// <item>PDF: AIP/IRM은 <c>/EncryptedPayload</c>·<c>/MicrosoftIRMService</c>(PDF 2.0 보호 래퍼), 표준 암호는
/// 트레일러 <c>/Encrypt</c>로 표시된다.</item>
/// </list>
/// 결과는 경로+수정시각으로 캐시하고 동시 검사를 제한해 보호 파일이 많은 폴더에서도 IO가 폭주하지 않는다.
/// </summary>
public static class FileProtectionDetector
{
    private static readonly HashSet<string> ProtectableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // OOXML
        "docx", "docm", "dotx", "dotm",
        "xlsx", "xlsm", "xlsb", "xltx", "xltm",
        "pptx", "pptm", "ppsx", "ppsm", "potx", "potm",
        // 레거시 Office (항상 CFBF — 내부 EncryptedPackage/DataSpaces 유무로 판별)
        "doc", "dot", "xls", "xlt", "xla", "ppt", "pot", "pps", "ppa",
        // PDF
        "pdf",
    };

    private static ReadOnlySpan<byte> CfbfSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;

    // 보호 검사 결과 캐시(경로+수정시각 키) — 폴더 재진입·재생성 시 같은 파일을 다시 열지 않는다.
    private static readonly LruCache<string, bool> Cache = new(capacity: 4096);

    // 동시 파일 오픈 제한 — 보호 파일이 많은 폴더에서 스레드풀/디스크 IO 폭주 방지(비동기 대기라 풀 스레드를 막지 않음).
    private static readonly SemaphoreSlim Gate = new(4, 4);

    /// <summary>헤더를 읽지 않고 빠르게 — 보호 가능성이 있는 형식(Office 또는 PDF)인지.</summary>
    public static bool MaybeProtectable(string extensionWithoutDot) =>
        !string.IsNullOrEmpty(extensionWithoutDot) && ProtectableExtensions.Contains(extensionWithoutDot);

    /// <summary>
    /// 보호 여부를 캐시 + 동시성 제한을 거쳐 검사한다. UI 경로용 — 세마포어 대기는 비동기라 스레드를 막지 않고,
    /// 실제 파일 오픈은 스레드풀에서 수행한다.
    /// </summary>
    public static async Task<bool> IsProtectedAsync(string path, string extensionWithoutDot, long modifiedTicks)
    {
        if (!MaybeProtectable(extensionWithoutDot))
        {
            return false;
        }

        var key = path + "|" + modifiedTicks.ToString(CultureInfo.InvariantCulture);
        if (Cache.TryGet(key, out var cached))
        {
            return cached;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Cache.TryGet(key, out cached))
            {
                return cached;
            }

            var result = await Task.Run(() => Detect(path)).ConfigureAwait(false);
            Cache.Set(key, result);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>캐시·동시성 제한 없이 즉시 검사한다(주로 테스트용). UI 경로는 <see cref="IsProtectedAsync"/>를 쓴다.</summary>
    public static bool IsProtected(string path, string extensionWithoutDot) =>
        MaybeProtectable(extensionWithoutDot) && Detect(path);

    /// <summary>매직 바이트로 형식을 판별해 분기한다 — 확장자가 아니라 실제 내용으로 판단(예: AIP가 .pdf를 CFBF로 감싸도 잡힘).</summary>
    private static bool Detect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[8];
            var read = stream.ReadAtLeast(magic, 8, throwOnEndOfStream: false);
            if (read >= 8 && magic.SequenceEqual(CfbfSignature))
            {
                return CompoundFileProbe.HasEncryptionStream(stream);
            }

            if (read >= PdfSignature.Length && magic[..PdfSignature.Length].SequenceEqual(PdfSignature))
            {
                return PdfHasProtection(stream);
            }

            return false; // 일반 OOXML(ZIP) 등 — 보호 아님
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// PDF 보호 판정: 앞부분(보호 래퍼 구조)에서 <c>/EncryptedPayload</c>·<c>/MicrosoftIRMService</c>(AIP/IRM),
    /// 트레일러(끝부분)에서 <c>/Encrypt</c>(표준 암호)를 찾는다. 네트워크 파일을 고려해 큰 창이지만 상한을 둔다.
    /// </summary>
    private static bool PdfHasProtection(Stream stream)
    {
        var length = stream.Length;
        var headSize = (int)Math.Min(256 * 1024, length);
        var head = new byte[headSize];
        stream.Seek(0, SeekOrigin.Begin);
        if (stream.ReadAtLeast(head, headSize, throwOnEndOfStream: false) < headSize)
        {
            return false;
        }

        var headSpan = head.AsSpan();
        if (headSpan.IndexOf("/EncryptedPayload"u8) >= 0 || headSpan.IndexOf("/MicrosoftIRMService"u8) >= 0)
        {
            return true;
        }

        if (length <= headSize)
        {
            // 작은 파일: 전체가 head에 들어왔으니 표준 암호 /Encrypt도 여기서 확인.
            return headSpan.IndexOf("/Encrypt"u8) >= 0;
        }

        // 큰 파일: 트레일러(끝 128KB)에서 /Encrypt.
        var tailSize = (int)Math.Min(128 * 1024, length);
        var tail = new byte[tailSize];
        stream.Seek(length - tailSize, SeekOrigin.Begin);
        return stream.ReadAtLeast(tail, tailSize, throwOnEndOfStream: false) >= tailSize
            && tail.AsSpan().IndexOf("/Encrypt"u8) >= 0;
    }
}
