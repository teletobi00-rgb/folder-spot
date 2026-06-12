using Explorer.Core.Sorting;

namespace Explorer.Core.Workspace;

public enum PaneSide
{
    Left,
    Right,
}

/// <summary>재시작 간 복원용 직렬화 모델 (settings.json에 저장).</summary>
public sealed record TabSession(string Path, SortColumn SortColumn, bool SortDescending)
{
    public TabState ToState() => new()
    {
        Path = Path,
        Sort = new SortDescriptor(SortColumn, SortDescending),
    };

    public static TabSession FromState(TabState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new TabSession(state.Path, state.Sort.Column, state.Sort.Descending);
    }
}

public sealed record PaneSession(IReadOnlyList<TabSession> Tabs, int ActiveTabIndex);

public sealed record WorkspaceSession(
    PaneSession Left,
    PaneSession Right,
    bool IsDualMode,
    PaneSide ActiveSide);
