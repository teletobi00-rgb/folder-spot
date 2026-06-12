namespace Explorer.Core.Sorting;

public enum SortColumn
{
    Name,
    Extension,
    Size,
    DateModified,
    DateCreated,
    Attributes,
}

public sealed record SortDescriptor(SortColumn Column, bool Descending)
{
    public static SortDescriptor Default { get; } = new(SortColumn.Name, Descending: false);

    /// <summary>같은 컬럼이면 방향을 뒤집고, 다른 컬럼이면 해당 컬럼 오름차순으로 바꾼다.</summary>
    public SortDescriptor Toggle(SortColumn column) =>
        column == Column ? this with { Descending = !Descending } : new SortDescriptor(column, Descending: false);
}
