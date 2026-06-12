using System.IO.Enumeration;

namespace Explorer.Core.FileSystem;

public sealed class FileSystemEnumerator : IFileSystemEnumerator
{
    public Task<IReadOnlyList<FileEntry>> ListAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var normalized = PathUtils.Normalize(directoryPath);
        return Task.Run<IReadOnlyList<FileEntry>>(
            () =>
            {
                if (!Directory.Exists(normalized))
                {
                    throw new DirectoryNotFoundException($"디렉터리를 찾을 수 없습니다: {normalized}");
                }

                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                    RecurseSubdirectories = false,
                };

                var enumerable = new FileSystemEnumerable<FileEntry>(
                    normalized,
                    (ref FileSystemEntry entry) => FileEntry.Create(
                        fullPath: entry.ToFullPath(),
                        name: entry.FileName.ToString(),
                        isDirectory: entry.IsDirectory,
                        size: entry.IsDirectory ? 0 : entry.Length,
                        dateModified: entry.LastWriteTimeUtc.LocalDateTime,
                        dateCreated: entry.CreationTimeUtc.LocalDateTime,
                        attributes: entry.Attributes),
                    options);

                var results = new List<FileEntry>(capacity: 256);
                foreach (var entry in enumerable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(entry);
                }

                return results;
            },
            cancellationToken);
    }
}
