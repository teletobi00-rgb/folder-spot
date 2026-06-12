namespace Explorer.Core.FileSystem;

/// <summary>파일/폴더 한 항목의 불변 스냅샷.</summary>
public sealed record FileEntry
{
    public required string FullPath { get; init; }

    public required string Name { get; init; }

    /// <summary>선행 점 없는 소문자 확장자. 폴더이거나 확장자가 없으면 빈 문자열.</summary>
    public string Extension { get; init; } = string.Empty;

    public long Size { get; init; }

    public DateTime DateModified { get; init; }

    public DateTime DateCreated { get; init; }

    public FileAttributes Attributes { get; init; }

    public bool IsDirectory { get; init; }

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;

    public bool IsSystem => (Attributes & FileAttributes.System) != 0;

    public static FileEntry Create(
        string fullPath,
        string name,
        bool isDirectory,
        long size,
        DateTime dateModified,
        DateTime dateCreated,
        FileAttributes attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new FileEntry
        {
            FullPath = fullPath,
            Name = name,
            IsDirectory = isDirectory,
            Extension = isDirectory ? string.Empty : NormalizeExtension(name),
            Size = isDirectory ? 0 : size,
            DateModified = dateModified,
            DateCreated = dateCreated,
            Attributes = attributes,
        };
    }

    private static string NormalizeExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : string.Empty;
    }
}
