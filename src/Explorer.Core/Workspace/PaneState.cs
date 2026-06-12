using System.Collections.Immutable;

namespace Explorer.Core.Workspace;

/// <summary>
/// 페인 하나의 불변 탭 컬렉션 상태. 모든 연산은 새 인스턴스를 반환하며,
/// 페인은 항상 1개 이상의 탭을 가진다는 불변식을 유지한다.
/// </summary>
public sealed record PaneState
{
    public ImmutableArray<TabState> Tabs { get; init; } = [];

    public int ActiveTabIndex { get; init; }

    public TabState ActiveTab => Tabs[ActiveTabIndex];

    public static PaneState Create(TabState initialTab)
    {
        ArgumentNullException.ThrowIfNull(initialTab);
        return new PaneState { Tabs = [initialTab], ActiveTabIndex = 0 };
    }

    /// <summary>탭을 끝에 추가한다 (기본: 새 탭 활성화).</summary>
    public PaneState AddTab(TabState tab, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var tabs = Tabs.Add(tab);
        return this with
        {
            Tabs = tabs,
            ActiveTabIndex = activate ? tabs.Length - 1 : ActiveTabIndex,
        };
    }

    /// <summary>지정 위치에 탭을 삽입한다.</summary>
    public PaneState InsertTab(int index, TabState tab, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var clamped = Math.Clamp(index, 0, Tabs.Length);
        var tabs = Tabs.Insert(clamped, tab);
        var active = activate
            ? clamped
            : ActiveTabIndex >= clamped ? ActiveTabIndex + 1 : ActiveTabIndex;
        return this with { Tabs = tabs, ActiveTabIndex = active };
    }

    /// <summary>탭을 닫는다. 마지막 남은 탭이면 변화 없음(페인은 항상 탭 ≥1).</summary>
    public PaneState CloseTab(int index)
    {
        if (Tabs.Length <= 1 || index < 0 || index >= Tabs.Length)
        {
            return this;
        }

        var tabs = Tabs.RemoveAt(index);
        var active = ActiveTabIndex switch
        {
            var current when current > index => current - 1,
            var current when current == index => Math.Min(index, tabs.Length - 1),
            _ => ActiveTabIndex,
        };
        return this with { Tabs = tabs, ActiveTabIndex = active };
    }

    public PaneState Activate(int index) =>
        index >= 0 && index < Tabs.Length ? this with { ActiveTabIndex = index } : this;

    /// <summary>활성 탭의 상태를 변환한다 (경로/정렬 동기화용).</summary>
    public PaneState UpdateActiveTab(Func<TabState, TabState> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var updated = transform(ActiveTab)
            ?? throw new InvalidOperationException("탭 변환 함수는 null을 반환할 수 없습니다.");
        return this with { Tabs = Tabs.SetItem(ActiveTabIndex, updated) };
    }

    /// <summary>탭 순서를 옮긴다. 활성 탭은 같은 탭을 계속 가리킨다.</summary>
    public PaneState MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tabs.Length || toIndex < 0 || toIndex >= Tabs.Length
            || fromIndex == toIndex)
        {
            return this;
        }

        var moving = Tabs[fromIndex];
        var tabs = Tabs.RemoveAt(fromIndex).Insert(toIndex, moving);

        var active = ActiveTabIndex;
        if (active == fromIndex)
        {
            active = toIndex;
        }
        else if (fromIndex < active && toIndex >= active)
        {
            active--;
        }
        else if (fromIndex > active && toIndex <= active)
        {
            active++;
        }

        return this with { Tabs = tabs, ActiveTabIndex = active };
    }
}
