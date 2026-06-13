using System.Collections.Immutable;

namespace Explorer.Preview;

public enum PreviewKind
{
    /// <summary>미리보기 대상 없음 (선택 해제/폴더 등).</summary>
    None,
    Image,
    Text,
    Media,
    Archive,
    Info,
    Error,
}

/// <summary>압축 파일 내 항목 한 줄.</summary>
public sealed record ArchiveEntryInfo(string Path, long Size, bool IsDirectory);

/// <summary>정보 미리보기의 한 행(레이블/값).</summary>
public sealed record InfoLine(string Label, string Value);

/// <summary>
/// 미리보기 한 건의 불변 결과. 종류에 따라 해당 페이로드만 채워진다.
/// View는 Kind에 따라 적절한 컨트롤(이미지/AvalonEdit/MediaElement/목록)을 선택한다.
/// </summary>
public sealed record PreviewResult
{
    public required PreviewKind Kind { get; init; }

    public required string FilePath { get; init; }

    /// <summary>표시 이름 (파일명).</summary>
    public string DisplayName { get; init; } = string.Empty;

    // --- Text ---
    public string? Text { get; init; }

    /// <summary>AvalonEdit 구문 강조용 언어 힌트 (예: "C#", "JSON"). 없으면 일반 텍스트.</summary>
    public string? LanguageHint { get; init; }

    public string? EncodingName { get; init; }

    /// <summary>크기 상한으로 잘렸는지.</summary>
    public bool Truncated { get; init; }

    // --- Archive ---
    public ImmutableArray<ArchiveEntryInfo> ArchiveEntries { get; init; } = [];

    public bool ArchiveTruncated { get; init; }

    // --- Info / fallback ---
    public ImmutableArray<InfoLine> InfoLines { get; init; } = [];

    // --- Error ---
    public string? ErrorMessage { get; init; }

    public static PreviewResult None(string filePath = "") => new()
    {
        Kind = PreviewKind.None,
        FilePath = filePath,
    };

    public static PreviewResult Error(string filePath, string message) => new()
    {
        Kind = PreviewKind.Error,
        FilePath = filePath,
        DisplayName = System.IO.Path.GetFileName(filePath),
        ErrorMessage = message,
    };
}
