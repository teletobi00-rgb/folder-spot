using System.IO.Enumeration;
using Explorer.Indexing.Index;

namespace Explorer.Indexing.Sources;

internal static class IndexPathUpdater
{
    private const int BatchSize = 5000;

    public static IReadOnlyList<IndexItem> FilterExcluded(IReadOnlyList<IndexItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var filtered = new List<IndexItem>(items.Count);
        foreach (var item in items)
        {
            if (!IsExcluded(item))
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    /// <summary>
    /// 경로 하위 트리를 현재 디스크 상태 기준으로 인덱스에 반영한다 — 디렉터리면 재귀 열거까지. <b>생성/이동</b>처럼
    /// 트리 전체가 새로 나타날 수 있는 경우에만 쓴다(변경 이벤트엔 <see cref="AddSinglePath"/> 사용).
    /// </summary>
    public static bool AddExistingPathTree(IFileIndex index, string fullPath, bool isDirectoryHint)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (IndexExclusions.IsExcludedPath(fullPath))
        {
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            AddKnownPath(index, fullPath, isDirectory: true);
            AddDirectoryChildren(index, fullPath);
            return true;
        }

        if (File.Exists(fullPath))
        {
            AddFile(index, fullPath);
            return true;
        }

        index.RemoveSubtree(fullPath);
        return false;
    }

    /// <summary>
    /// 경로 한 건만 현재 디스크 상태 기준으로 반영한다 — <b>재귀 없음</b>. 변경(Changed/Modified) 이벤트용:
    /// 디렉터리 자식 추가/삭제는 각자 자기 이벤트로 처리되므로 부모 트리를 다시 걷지 않는다.
    /// </summary>
    public static bool AddSinglePath(IFileIndex index, string fullPath, bool isDirectoryHint)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (IndexExclusions.IsExcludedPath(fullPath))
        {
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            AddKnownPath(index, fullPath, isDirectory: true);
            return true;
        }

        if (File.Exists(fullPath))
        {
            AddFile(index, fullPath);
            return true;
        }

        index.RemoveSubtree(fullPath);
        return false;
    }

    public static void AddKnownPath(IFileIndex index, string fullPath, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (IndexExclusions.IsExcludedPath(fullPath))
        {
            return;
        }

        if (TryCreateItem(fullPath, isDirectory, size: 0, modifiedTicks: 0, out var item))
        {
            index.AddOrUpdate(item);
        }
    }

    private static bool IsExcluded(in IndexItem item)
    {
        var fullPath = Path.Combine(item.ParentPath, item.Name);
        return IndexExclusions.IsExcludedPath(fullPath);
    }

    private static void AddFile(IFileIndex index, string fullPath)
    {
        long size = 0;
        long modifiedTicks = 0;
        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
            modifiedTicks = info.LastWriteTime.Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (TryCreateItem(fullPath, isDirectory: false, size, modifiedTicks, out var item))
        {
            index.AddOrUpdate(item);
        }
    }

    private static void AddDirectoryChildren(IFileIndex index, string rootPath)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            RecurseSubdirectories = true,
        };

        var enumerable = new FileSystemEnumerable<IndexItem>(
            rootPath,
            (ref FileSystemEntry entry) => new IndexItem(
                ParentPath: entry.Directory.ToString(),
                Name: entry.FileName.ToString(),
                IsDirectory: entry.IsDirectory,
                Size: entry.IsDirectory ? 0 : entry.Length,
                ModifiedTicks: entry.LastWriteTimeUtc.LocalDateTime.Ticks),
            options)
        {
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString()),
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory
                || !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString()),
        };

        try
        {
            var batch = new List<IndexItem>(BatchSize);
            foreach (var item in enumerable)
            {
                batch.Add(item);
                if (batch.Count >= BatchSize)
                {
                    index.AddBatch(FilterExcluded(batch));
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                index.AddBatch(FilterExcluded(batch));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private static bool TryCreateItem(
        string fullPath,
        bool isDirectory,
        long size,
        long modifiedTicks,
        out IndexItem item)
    {
        var parent = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent) || name.Length == 0)
        {
            item = default;
            return false;
        }

        item = new IndexItem(parent, name, isDirectory, size, modifiedTicks);
        return true;
    }
}
