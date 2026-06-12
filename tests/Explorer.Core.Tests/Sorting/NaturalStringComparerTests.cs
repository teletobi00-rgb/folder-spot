using System.Globalization;
using Explorer.Core.Sorting;
using FluentAssertions;

namespace Explorer.Core.Tests.Sorting;

public sealed class NaturalStringComparerTests
{
    private static readonly NaturalStringComparer Comparer = NaturalStringComparer.Instance;

    [Theory]
    [InlineData("file2", "file10")]
    [InlineData("file9", "file10")]
    [InlineData("a2b", "a10b")]
    [InlineData("1", "a")]
    [InlineData("가나다", "나다라")]
    [InlineData("apple", "banana")]
    public void Compare_OrdersNaturally(string smaller, string larger)
    {
        Comparer.Compare(smaller, larger).Should().BeNegative();
        Comparer.Compare(larger, smaller).Should().BePositive();
    }

    [Fact]
    public void Compare_IsCaseInsensitive()
    {
        Comparer.Compare("FILE2", "file10").Should().BeNegative();
        Comparer.Compare("abc", "ABC").Should().Be(0);
    }

    [Fact]
    public void Compare_LeadingZeros_NumericallyEqual_IsDeterministic()
    {
        // 수치가 같으면 원본 자릿수가 짧은 쪽이 먼저
        Comparer.Compare("file2", "file002").Should().BeNegative();
        Comparer.Compare("file002", "file2").Should().BePositive();
    }

    [Fact]
    public void Compare_PrefixComesFirst()
    {
        Comparer.Compare("file", "file2").Should().BeNegative();
    }

    [Fact]
    public void Compare_HandlesNulls()
    {
        Comparer.Compare(null, "a").Should().BeNegative();
        Comparer.Compare("a", null).Should().BePositive();
        Comparer.Compare(null, null).Should().Be(0);
    }

    [Fact]
    public void Sort_InKoreanCulture_HangulComesBeforeLatin()
    {
        // 비교자는 CurrentCulture를 따른다. 한국어 로캘은 한글을 라틴보다 먼저 배치한다(Windows 탐색기와 동일).
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ko-KR");
            var names = new List<string> { "file10.txt", "file2.txt", "File1.txt", "가나.txt", "10.txt", "2.txt" };

            names.Sort(Comparer!);

            names.Should().Equal("2.txt", "10.txt", "가나.txt", "File1.txt", "file2.txt", "file10.txt");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
