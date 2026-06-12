using Explorer.Core.FileSystem;

namespace Explorer.Core.FileOperations;

public enum ConflictDecision
{
    Overwrite,
    Skip,
    KeepBoth,
}

/// <summary>대상 폴더에 같은 이름이 이미 있는 원본 항목 하나.</summary>
public sealed record FileConflict
{
    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required bool IsDirectory { get; init; }

    public long SourceSize { get; init; }

    public long TargetSize { get; init; }

    public DateTime SourceModified { get; init; }

    public DateTime TargetModified { get; init; }

    public string Name => Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar));
}

/// <summary>복사/이동 시작 전에 대상 폴더와의 이름 충돌을 미리 찾아낸다.</summary>
public static class ConflictScanner
{
    public static IReadOnlyList<FileConflict> Scan(IReadOnlyList<string> sourcePaths, string destinationDir)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var destination = PathUtils.Normalize(destinationDir);

        var conflicts = new List<FileConflict>();
        foreach (var source in sourcePaths)
        {
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var target = Path.Combine(destination, name);
            var sourceIsDir = Directory.Exists(source);
            var targetFileExists = File.Exists(target);
            var targetDirExists = Directory.Exists(target);
            if (!targetFileExists && !targetDirExists)
            {
                continue;
            }

            conflicts.Add(new FileConflict
            {
                SourcePath = source,
                TargetPath = target,
                IsDirectory = sourceIsDir || targetDirExists,
                SourceSize = SafeFileSize(source, sourceIsDir),
                TargetSize = SafeFileSize(target, targetDirExists),
                SourceModified = SafeModified(source),
                TargetModified = SafeModified(target),
            });
        }

        return conflicts;
    }

    private static long SafeFileSize(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return 0;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTime SafeModified(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
