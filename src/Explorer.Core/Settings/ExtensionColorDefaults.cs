using System.Collections.Immutable;

namespace Explorer.Core.Settings;

/// <summary>
/// 확장자별 파일명 글자색 기본 프리셋(확장자는 점 없는 소문자, 값은 #RRGGBB).
/// 다크/라이트 양쪽에서 읽히도록 중간 톤으로 골랐다. 사용자가 설정에서 덮어쓸 수 있다.
/// </summary>
public static class ExtensionColorDefaults
{
    public static ImmutableDictionary<string, string> Map { get; } = BuildDefaults();
    public static ImmutableDictionary<string, string> DarkMap { get; } = BuildDarkDefaults();
    public static ImmutableDictionary<string, string> LightMap { get; } = BuildLightDefaults();

    private static ImmutableDictionary<string, string> BuildDefaults()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        // 기본 맵은 중간값 유지 (하위 호환성용)
        AddAll(builder, "#1FA463", "xlsx", "xls", "xlsm", "xlsb", "xltx", "xlt", "csv");
        AddAll(builder, "#4178D4", "docx", "doc", "docm", "dotx", "dot", "rtf", "hwp", "hwpx");
        AddAll(builder, "#E8730C", "pptx", "ppt", "pptm", "ppsx", "pps");
        AddAll(builder, "#E0492E", "pdf");
        AddAll(builder, "#B06FD6", "png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff");
        AddAll(builder, "#C9952B", "zip", "7z", "rar", "tar", "gz", "bz2");

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, string> BuildDarkDefaults()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        // 다크 테마: 밝고 맑은 파스텔 톤
        AddAll(builder, "#21A366", "xlsx", "xls", "xlsm", "xlsb", "xltx", "xlt", "csv");
        AddAll(builder, "#5C93FC", "docx", "doc", "docm", "dotx", "dot", "rtf", "hwp", "hwpx");
        AddAll(builder, "#F28C38", "pptx", "ppt", "pptm", "ppsx", "pps");
        AddAll(builder, "#F25F4C", "pdf");
        AddAll(builder, "#C88AFA", "png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff");
        AddAll(builder, "#D4A33B", "zip", "7z", "rar", "tar", "gz", "bz2");
        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, string> BuildLightDefaults()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        // 라이트 테마: 가독성을 위해 더 깊고 짙은 톤
        AddAll(builder, "#107C41", "xlsx", "xls", "xlsm", "xlsb", "xltx", "xlt", "csv");
        AddAll(builder, "#185ABD", "docx", "doc", "docm", "dotx", "dot", "rtf", "hwp", "hwpx");
        AddAll(builder, "#C45500", "pptx", "ppt", "pptm", "ppsx", "pps");
        AddAll(builder, "#B32A15", "pdf");
        AddAll(builder, "#8544B3", "png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff");
        AddAll(builder, "#9E6E13", "zip", "7z", "rar", "tar", "gz", "bz2");
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
