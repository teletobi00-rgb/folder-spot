namespace Explorer.Core.FileSystem;

/// <summary>경로 검증/정규화의 단일 진입점. 모든 외부 입력 경로는 여기를 거친다.</summary>
public static class PathUtils
{
    public static bool IsAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path.Trim());

    /// <summary>
    /// 절대 경로를 정규화한다(공백 제거, "." / ".." 해석, 루트 외 후행 구분자 제거).
    /// 상대 경로나 잘못된 경로는 <see cref="ArgumentException"/>.
    /// </summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException($"절대 경로가 아닙니다: {path}", nameof(path));
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"잘못된 경로입니다: {path}", nameof(path), ex);
        }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Length > root.Length)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }

    /// <summary>부모 디렉터리 경로. 루트(드라이브/UNC 공유)면 null.</summary>
    public static string? GetParent(string path) => Path.GetDirectoryName(Normalize(path));

    /// <summary>path가 ancestor 자신이거나 그 하위 경로인지 (대소문자 무시).</summary>
    public static bool IsSameOrDescendant(string ancestor, string path)
    {
        var normalizedAncestor = Normalize(ancestor);
        var normalizedPath = Normalize(path);

        if (string.Equals(normalizedAncestor, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedAncestor
            : normalizedAncestor + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
