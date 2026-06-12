using Explorer.Core.Caching;
using FluentAssertions;

namespace Explorer.Core.Tests.Caching;

public sealed class LruCacheTests
{
    [Fact]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(capacity: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Set("c", 3);

        cache.TryGet("a", out _).Should().BeFalse();
        cache.TryGet("b", out _).Should().BeTrue();
        cache.TryGet("c", out _).Should().BeTrue();
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void TryGet_RefreshesRecency()
    {
        var cache = new LruCache<string, int>(capacity: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.TryGet("a", out _).Should().BeTrue();
        cache.Set("c", 3);

        cache.TryGet("a", out _).Should().BeTrue("a를 최근에 읽었으므로 b가 제거돼야 함");
        cache.TryGet("b", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValueWithoutGrowing()
    {
        var cache = new LruCache<string, int>(capacity: 2);
        cache.Set("a", 1);

        cache.Set("a", 99);

        cache.TryGet("a", out var value).Should().BeTrue();
        value.Should().Be(99);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void Ctor_NonPositiveCapacity_Throws()
    {
        var act = () => new LruCache<string, int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
