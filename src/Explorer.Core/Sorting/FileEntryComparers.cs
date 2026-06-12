using Explorer.Core.FileSystem;

namespace Explorer.Core.Sorting;

/// <summary>정렬 설정에 따른 FileEntry 비교자. 폴더는 방향과 무관하게 항상 파일보다 먼저.</summary>
public static class FileEntryComparers
{
    public static IComparer<FileEntry> Create(SortDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new FileEntryComparer(descriptor);
    }

    private sealed class FileEntryComparer(SortDescriptor descriptor) : IComparer<FileEntry>
    {
        public int Compare(FileEntry? x, FileEntry? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            if (x.IsDirectory != y.IsDirectory)
            {
                return x.IsDirectory ? -1 : 1;
            }

            var result = CompareByColumn(x, y);
            if (descriptor.Descending)
            {
                result = -result;
            }

            return result != 0 ? result : NaturalStringComparer.Instance.Compare(x.Name, y.Name);
        }

        private int CompareByColumn(FileEntry x, FileEntry y) => descriptor.Column switch
        {
            SortColumn.Name => NaturalStringComparer.Instance.Compare(x.Name, y.Name),
            SortColumn.Extension => string.Compare(x.Extension, y.Extension, StringComparison.OrdinalIgnoreCase),
            SortColumn.Size => x.Size.CompareTo(y.Size),
            SortColumn.DateModified => x.DateModified.CompareTo(y.DateModified),
            SortColumn.DateCreated => x.DateCreated.CompareTo(y.DateCreated),
            SortColumn.Attributes => ((int)x.Attributes).CompareTo((int)y.Attributes),
            _ => 0,
        };
    }
}
