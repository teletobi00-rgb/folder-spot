using System.IO;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Explorer.App.Services;

/// <summary>압축 생성(ZIP, BCL)과 해제(zip/7z/rar/tar/gz, SharpCompress).</summary>
public static class ArchiveService
{
    private static readonly string[] ArchiveExtensions =
        [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"];

    /// <summary>해제 가능한 아카이브 확장자인지.</summary>
    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>선택 항목들을 destinationDir에 ZIP으로 묶는다. 만든 zip 경로를 반환한다.</summary>
    public static Task<string> CreateZipAsync(
        IReadOnlyList<string> sourcePaths, string destinationDir, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var zipName = sourcePaths.Count == 1
                ? Path.GetFileNameWithoutExtension(sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".zip"
                : "압축.zip";
            var zipPath = MakeUniquePath(Path.Combine(destinationDir, zipName));

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var path in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
                }
                else if (Directory.Exists(path))
                {
                    var prefix = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(path, file).Replace(Path.DirectorySeparatorChar, '/');
                        zip.CreateEntryFromFile(file, $"{prefix}/{relative}", CompressionLevel.Optimal);
                    }
                }
            }

            return zipPath;
        }, cancellationToken);
    }

    /// <summary>아카이브를 그 이름의 하위 폴더로 푼다. 푼 폴더 경로를 반환한다.</summary>
    public static Task<string> ExtractAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var parent = Path.GetDirectoryName(archivePath)
                ?? throw new DirectoryNotFoundException("상위 폴더를 찾을 수 없습니다.");
            var destination = MakeUniquePath(Path.Combine(parent, Path.GetFileNameWithoutExtension(archivePath)));
            Directory.CreateDirectory(destination);

            using var archive = ArchiveFactory.Open(archivePath);
            var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destination, options);
                }
            }

            return destination;
        }, cancellationToken);
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
