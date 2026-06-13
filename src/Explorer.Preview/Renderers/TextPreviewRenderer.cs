using System.Text;
using Microsoft.Extensions.Logging;

namespace Explorer.Preview.Renderers;

/// <summary>텍스트/코드 — 인코딩 감지(BOM + UTF-8 검증) 후 크기 상한까지 읽는다.</summary>
public sealed class TextPreviewRenderer : IPreviewRenderer
{
    private const int MaxBytes = 1024 * 1024; // 1MB 상한

    // 확장자 → AvalonEdit 구문 강조 언어 힌트
    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "C#", ["js"] = "JavaScript", ["ts"] = "JavaScript", ["json"] = "JSON",
        ["xml"] = "XML", ["xaml"] = "XML", ["html"] = "HTML", ["htm"] = "HTML",
        ["css"] = "CSS", ["py"] = "Python", ["java"] = "Java", ["cpp"] = "C++",
        ["c"] = "C++", ["h"] = "C++", ["hpp"] = "C++", ["md"] = "MarkDown",
        ["sql"] = "SQL", ["ps1"] = "PowerShell", ["php"] = "PHP", ["vb"] = "VBNET",
        ["bat"] = "DOS", ["cmd"] = "DOS",
    };

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "log", "ini", "cfg", "conf", "yml", "yaml", "csv", "tsv", "gitignore",
        "editorconfig", "props", "targets", "sln", "csproj", "toml", "env", "sh", "rs", "go",
    };

    private readonly ILogger<TextPreviewRenderer> _logger;

    public TextPreviewRenderer(ILogger<TextPreviewRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public bool CanRender(string extension) =>
        LanguageByExtension.ContainsKey(extension) || PlainTextExtensions.Contains(extension);

    public async Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);

            var length = stream.Length;
            var truncated = length > MaxBytes;
            var toRead = (int)Math.Min(length, MaxBytes);
            var buffer = new byte[toRead];
            var read = await stream.ReadAtLeastAsync(buffer, toRead, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);

            var (text, encodingName) = Decode(buffer.AsSpan(0, read));
            var extension = GetExtension(filePath);

            return new PreviewResult
            {
                Kind = PreviewKind.Text,
                FilePath = filePath,
                DisplayName = Path.GetFileName(filePath),
                Text = text,
                EncodingName = encodingName,
                LanguageHint = LanguageByExtension.GetValueOrDefault(extension),
                Truncated = truncated,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "텍스트 미리보기 실패: {Path}", filePath);
            return PreviewResult.Error(filePath, $"파일을 읽을 수 없습니다: {ex.Message}");
        }
    }

    private static string GetExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.Length > 1 ? ext[1..].ToLowerInvariant() : string.Empty;
    }

    /// <summary>BOM 우선, 없으면 UTF-8 엄격 검증 → 실패 시 Latin1 폴백.</summary>
    private static (string Text, string EncodingName) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes[3..]), "UTF-8 BOM");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes[2..]), "UTF-16 LE");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes[2..]), "UTF-16 BE");
        }

        try
        {
            // 1MB 상한으로 잘릴 때 마지막 UTF-8 멀티바이트 시퀀스가 쪼개지면 전체가 Latin-1로
            // 오판될 수 있다 — 불완전한 꼬리 바이트를 제외하고 검증한다.
            var safe = bytes[..SafeUtf8Length(bytes)];
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strict.GetString(safe), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), "Latin-1");
        }
    }

    /// <summary>버퍼 끝의 불완전한 UTF-8 선행 시퀀스를 잘라낸 안전 길이를 돌려준다.</summary>
    private static int SafeUtf8Length(ReadOnlySpan<byte> bytes)
    {
        for (var i = bytes.Length - 1; i >= 0 && i >= bytes.Length - 3; i--)
        {
            var b = bytes[i];
            if ((b & 0x80) == 0)
            {
                return bytes.Length; // ASCII — 꼬리 정상
            }

            if ((b & 0xC0) == 0x80)
            {
                continue; // 연속 바이트 — 더 앞의 선행 바이트를 본다
            }

            // 선행 바이트: 기대 길이가 버퍼 안에 들어오면 정상, 아니면 여기서 자른다.
            var seqLen = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : 4;
            return i + seqLen <= bytes.Length ? bytes.Length : i;
        }

        return bytes.Length;
    }
}
