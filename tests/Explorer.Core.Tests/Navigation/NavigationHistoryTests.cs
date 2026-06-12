using Explorer.Core.Navigation;
using FluentAssertions;

namespace Explorer.Core.Tests.Navigation;

public sealed class NavigationHistoryTests
{
    [Fact]
    public void Empty_HasNoCurrentAndCannotMove()
    {
        var history = NavigationHistory.Empty;

        history.Current.Should().BeNull();
        history.CanGoBack.Should().BeFalse();
        history.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void Visit_AppendsAndMovesCurrent()
    {
        var history = NavigationHistory.Empty.Visit(@"C:\a").Visit(@"C:\b");

        history.Current.Should().Be(@"C:\b");
        history.CanGoBack.Should().BeTrue();
        history.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void Visit_SameAsCurrent_IsNoOp()
    {
        var history = NavigationHistory.Empty.Visit(@"C:\a");
        var revisited = history.Visit(@"C:\a");

        revisited.Should().BeSameAs(history);
    }

    [Fact]
    public void GoBackAndForward_MoveWithoutLosingEntries()
    {
        var history = NavigationHistory.Empty.Visit(@"C:\a").Visit(@"C:\b").Visit(@"C:\c");

        var back = history.GoBack();
        back.Current.Should().Be(@"C:\b");
        back.CanGoForward.Should().BeTrue();

        var forward = back.GoForward();
        forward.Current.Should().Be(@"C:\c");
    }

    [Fact]
    public void Visit_AfterGoBack_TruncatesForwardTail()
    {
        var history = NavigationHistory.Empty.Visit(@"C:\a").Visit(@"C:\b").Visit(@"C:\c")
            .GoBack()
            .GoBack()
            .Visit(@"C:\new");

        history.Current.Should().Be(@"C:\new");
        history.CanGoForward.Should().BeFalse();
        history.Entries.Should().Equal(@"C:\a", @"C:\new");
    }

    [Fact]
    public void GoBack_AtStart_IsNoOp()
    {
        var history = NavigationHistory.Empty.Visit(@"C:\a");

        history.GoBack().Should().BeSameAs(history);
    }

    [Fact]
    public void OriginalInstance_IsNotMutatedByOperations()
    {
        var original = NavigationHistory.Empty.Visit(@"C:\a");

        _ = original.Visit(@"C:\b");

        original.Current.Should().Be(@"C:\a");
        original.Entries.Should().HaveCount(1);
    }
}
