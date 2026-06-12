using Explorer.Core.FileSystem;

namespace Explorer.Shell.Icons;

/// <summary>아이콘 캐시 키 정책: 일반 파일은 확장자 단위, 개별 아이콘을 가진 종류는 경로 단위.</summary>
public static class IconCacheKey
{
    private static readonly HashSet<string> PerPathExtensions = new(StringComparer.Ordinal)
    {
        "exe", "lnk", "ico", "url", "cur", "appref-ms",
    };

    public static string For(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsDirectory)
        {
            // 알려진 한계: desktop.ini 커스텀 폴더 아이콘은 무시되고 모든 폴더가 기본 아이콘을 공유한다.
            return "<dir>";
        }

        if (entry.Extension.Length == 0)
        {
            return "<none>";
        }

        return PerPathExtensions.Contains(entry.Extension)
            ? "p:" + entry.FullPath
            : "e:" + entry.Extension;
    }

    /// <summary>확장자 단위 키인지 (true면 실제 파일 접근 없이 더미 이름으로 아이콘을 조회할 수 있다).</summary>
    public static bool IsExtensionScoped(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !entry.IsDirectory && !PerPathExtensions.Contains(entry.Extension);
    }
}
