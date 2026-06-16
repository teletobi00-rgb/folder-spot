using Explorer.Indexing.Index;
using FluentAssertions;

namespace Explorer.Indexing.Tests;

public sealed class FileIndexCatalogTests
{
    [Fact]
    public void Swap_KeepsPreviousIndexAliveUntilLeaseIsReleased()
    {
        var catalog = new FileIndexCatalog();
        var lease = catalog.Acquire();
        var oldIndex = lease.Index;
        oldIndex.AddOrUpdate(new IndexItem(@"C:\held", "old.txt", false, 1, 0));

        catalog.Swap(new FileIndex());

        oldIndex.Search("old", 10).Should().ContainSingle("활성 검색 lease가 끝나기 전까지 이전 인덱스가 살아 있어야 한다");

        lease.Dispose();
        var searchAfterRelease = () => oldIndex.Search("old", 10);
        searchAfterRelease.Should().Throw<ObjectDisposedException>();

        catalog.Dispose();
    }
}
