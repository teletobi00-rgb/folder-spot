using System.Globalization;
using System.IO;
using Explorer.Core.Caching;

namespace Explorer.App.Services;

/// <summary>
/// 보호(암호화/AIP·IRM·권한 제한) 파일 감지:
/// <list type="bullet">
/// <item>OOXML(docx/xlsx/pptx 등): AIP/암호로 보호되면 ZIP이 아니라 CFBF(OLE 복합 문서, EncryptedPackage)로
/// 저장되므로 헤더가 CFBF면 보호된 것이다.</item>
/// <item>PDF: 트레일러에 <c>/Encrypt</c> 항목이 있으면 암호화/권한 제한(열기 암호 또는 소유자 권한 제한)이다.</item>
/// </list>
/// 결과는 경로+수정시각으로 캐시해 폴더 재진입·스크롤마다 파일을 다시 열지 않으며, 동시 검사를 제한해
/// 보호 파일이 많은 폴더에서 디스크 IO가 폭주하지 않게 한다.
/// (라벨만 있고 암호화 없는 OOXML, 레거시 doc/xls/ppt는 v1 범위 밖.)
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

    // 보호 검사 결과 캐시(경로+수정시각 키) — 폴더 재진입·재생성 시 같은 파일을 다시 열지 않는다.
    private static readonly LruCache<string, bool> Cache = new(capacity: 4096);

    // 동시 파일 오픈 제한 — 보호 파일이 많은 폴더에서 스레드풀/디스크 IO 폭주 방지(비동기 대기라 풀 스레드를 막지 않음).
    private static readonly SemaphoreSlim Gate = new(4, 4);

    /// <summary>헤더를 읽지 않고 빠르게 — 보호 가능성이 있는 형식(OOXML 또는 PDF)인지.</summary>
    public static bool MaybeProtectable(string extensionWithoutDot) =>
        !string.IsNullOrEmpty(extensionWithoutDot)
        && (OoxmlExtensions.Contains(extensionWithoutDot)
            || string.Equals(extensionWithoutDot, "pdf", StringComparison.OrdinalIgnoreCase));

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
            // 대기 중 다른 호출이 채웠을 수 있으니 한 번 더 확인.
            if (Cache.TryGet(key, out cached))
            {
                return cached;
            }

            var result = await Task.Run(() => Detect(path, extensionWithoutDot)).ConfigureAwait(false);
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
        MaybeProtectable(extensionWithoutDot) && Detect(path, extensionWithoutDot);

    private static bool Detect(string path, string extensionWithoutDot)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return string.Equals(extensionWithoutDot, "pdf", StringComparison.OrdinalIgnoreCase)
                ? IsEncryptedPdf(stream)
                : IsCfbf(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsCfbf(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        return stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) >= 8
            && header.SequenceEqual(CfbfSignature);
    }

    private static ReadOnlySpan<byte> EncryptToken => "/Encrypt"u8;

    /// <summary>
    /// PDF 암호화 판정: 선두 1KB에서 <c>%PDF-</c> 시그니처를 확인하고(여기서 선형화 PDF의 앞쪽 /Encrypt도 커버),
    /// 트레일러가 있는 파일 끝 64KB에서 <c>/Encrypt</c> 토큰을 찾는다. 암호화된 PDF는 트레일러가 암호화 딕셔너리를
    /// <c>/Encrypt n g R</c>로 참조한다. 배지 표시 목적이라 존재 여부만 본다(권한 플래그 파싱은 하지 않음).
    /// </summary>
    private static bool IsEncryptedPdf(Stream stream)
    {
        var length = stream.Length;
        if (length < 8)
        {
            return false;
        }

        // 1) 선두: %PDF- 시그니처(선두 1KB 내) + 선형화 PDF의 앞쪽 /Encrypt.
        var headSize = (int)Math.Min(1024, length);
        var head = new byte[headSize];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(head, 0, headSize);
        if (head.AsSpan().IndexOf("%PDF-"u8) < 0)
        {
            return false;
        }

        if (head.AsSpan().IndexOf(EncryptToken) >= 0)
        {
            return true;
        }

        // 2) 트레일러(파일 끝 64KB)에서 /Encrypt 검색.
        const int TailWindow = 64 * 1024;
        var tailSize = (int)Math.Min(TailWindow, length);
        var tail = new byte[tailSize];
        stream.Seek(length - tailSize, SeekOrigin.Begin);
        stream.ReadExactly(tail, 0, tailSize);
        return tail.AsSpan().IndexOf(EncryptToken) >= 0;
    }
}
