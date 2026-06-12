namespace Explorer.Core.FileSystem;

public enum DropOperation
{
    None,
    Copy,
    Move,
}

/// <summary>드래그앤드롭 효과 결정과 유효성 검증 (탐색기 관례).</summary>
public static class DropRules
{
    /// <summary>Ctrl=복사, Shift=이동, 무수정자는 같은 볼륨이면 이동/다른 볼륨이면 복사.</summary>
    public static DropOperation Resolve(bool copyModifier, bool moveModifier, bool sameVolume)
    {
        if (copyModifier)
        {
            return DropOperation.Copy;
        }

        if (moveModifier)
        {
            return DropOperation.Move;
        }

        return sameVolume ? DropOperation.Move : DropOperation.Copy;
    }

    public static bool IsSameVolume(string pathA, string pathB) =>
        string.Equals(Path.GetPathRoot(pathA), Path.GetPathRoot(pathB), StringComparison.OrdinalIgnoreCase);

    /// <summary>자기 자신/하위 폴더로의 드롭, 같은 폴더로의 이동(무의미) 등을 차단한다.</summary>
    public static bool CanDrop(IReadOnlyList<string> sourcePaths, string targetDir, DropOperation operation)
    {
        if (operation == DropOperation.None || sourcePaths is not { Count: > 0 })
        {
            return false;
        }

        string normalizedTarget;
        try
        {
            normalizedTarget = PathUtils.Normalize(targetDir);
        }
        catch (ArgumentException)
        {
            return false;
        }

        foreach (var source in sourcePaths)
        {
            if (PathUtils.IsSameOrDescendant(source, normalizedTarget))
            {
                return false;
            }
        }

        if (operation == DropOperation.Move
            && sourcePaths.All(s => string.Equals(PathUtils.GetParent(s), normalizedTarget, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }
}
