using System.Collections.Immutable;

namespace Explorer.Core.Settings;

/// <summary>
/// 확장자별 파일명 글자색 기본 프리셋(확장자는 점 없는 소문자, 값은 #RRGGBB).
/// 다크/라이트 양쪽에서 읽히도록 중간 톤으로 골랐다. 사용자가 설정에서 덮어쓸 수 있다.
/// </summary>
public static class ExtensionColorDefaults
{
    public static ImmutableDictionary<string, string> Map { get; } = BuildDefaults();

    private static ImmutableDictionary<string, string> BuildDefaults()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        // 엑셀 — 녹색 계열
        AddAll(builder, "#1FA463", "xlsx", "xls", "xlsm", "xlsb", "xltx", "xlt", "csv");
        // 워드 — 푸른색 계열
        AddAll(builder, "#4178D4", "docx", "doc", "docm", "dotx", "dot", "rtf", "hwp", "hwpx");
        // 파워포인트 — 주황 계열
        AddAll(builder, "#E8730C", "pptx", "ppt", "pptm", "ppsx", "pps");
        // PDF — 붉은 계열
        AddAll(builder, "#E0492E", "pdf");
        // 이미지 — 보라 계열
        AddAll(builder, "#B06FD6", "png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff");
        // 압축 — 황갈 계열
        AddAll(builder, "#C9952B", "zip", "7z", "rar", "tar", "gz", "bz2");

        return builder.ToImmutable();
    }

    private static void AddAll(ImmutableDictionary<string, string>.Builder builder, string color, params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            builder[ext] = color;
        }
    }
}
