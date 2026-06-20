using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Explorer.Core.FileSystem;

public sealed class FileSystemEnumerator : IFileSystemEnumerator
{
    private const int BatchSize = 256;

    public Task<IReadOnlyList<FileEntry>> ListAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var normalized = PathUtils.Normalize(directoryPath);
        return Task.Run<IReadOnlyList<FileEntry>>(
            () =>
            {
                EnsureExists(normalized);
                var results = new List<FileEntry>(capacity: 256);
                foreach (var entry in Build(normalized))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(entry);
                }

                return results;
            },
            cancellationToken);
    }

    public async IAsyncEnumerable<IReadOnlyList<FileEntry>> StreamAsync(
        string directoryPath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalized = PathUtils.Normalize(directoryPath);
        var channel = Channel.CreateUnbounded<IReadOnlyList<FileEntry>>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // 열거(동기 FindNextFile 루프)는 백그라운드 스레드에서 돌리고, 배치를 채널로 흘려보낸다.
        var producer = Task.Run(
            () =>
            {
                try
                {
                    EnsureExists(normalized);
                    var batch = new List<FileEntry>(BatchSize);
                    foreach (var entry in Build(normalized))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        batch.Add(entry);
                        if (batch.Count >= BatchSize)
                        {
                            channel.Writer.TryWrite(batch);
                            batch = new List<FileEntry>(BatchSize);
                        }
                    }

                    if (batch.Count > 0)
                    {
                        channel.Writer.TryWrite(batch);
                    }

                    channel.Writer.Complete();
                }
                catch (Exception ex)
                {
                    // DirectoryNotFound/UnauthorizedAccess 등을 소비자(ReadAllAsync)로 전파한다.
                    channel.Writer.Complete(ex);
                }
            },
            CancellationToken.None);

        await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return batch;
        }

        await producer.ConfigureAwait(false);
    }

    private static void EnsureExists(string normalizedPath)
    {
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"디렉터리를 찾을 수 없습니다: {normalizedPath}");
        }
    }

    private static FileSystemEnumerable<FileEntry> Build(string normalizedPath)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
        };

        return new FileSystemEnumerable<FileEntry>(
            normalizedPath,
            (ref FileSystemEntry entry) => FileEntry.Create(
                fullPath: entry.ToFullPath(),
                name: entry.FileName.ToString(),
                isDirectory: entry.IsDirectory,
                size: entry.IsDirectory ? 0 : entry.Length,
                dateModified: entry.LastWriteTimeUtc.LocalDateTime,
                dateCreated: entry.CreationTimeUtc.LocalDateTime,
                attributes: entry.Attributes),
            options);
    }
}
