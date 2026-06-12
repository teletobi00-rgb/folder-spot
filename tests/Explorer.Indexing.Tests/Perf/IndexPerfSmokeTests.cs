using System.Diagnostics;
using Explorer.Indexing.Index;
using FluentAssertions;
using Xunit.Abstractions;

namespace Explorer.Indexing.Tests.Perf;

/// <summary>합성 20만 항목으로 메모리 사용량과 as-you-type 검색 지연을 측정한다.</summary>
public sealed class IndexPerfSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public void TwoHundredThousandEntries_MemoryAndSearchLatency()
    {
        const int dirCount = 5_000;
        const int filesPerDir = 39; // 총 ≈ 200k 노드

        var before = GC.GetTotalMemory(forceFullCollection: true);
        using var index = new FileIndex();

        var stopwatch = Stopwatch.StartNew();
        var batch = new List<IndexItem>(8192);
        for (var d = 0; d < dirCount; d++)
        {
            var parent = $@"C:\synthetic\group{d % 50}\dir{d:0000}";
            for (var f = 0; f < filesPerDir; f++)
            {
                batch.Add(new IndexItem(parent, $"문서-document-{d:0000}-{f:00}.txt", false, f * 100, 0));
                if (batch.Count >= 8192)
                {
                    index.AddBatch(batch);
                    batch.Clear();
                }
            }
        }

        index.AddBatch(batch);
        var buildMs = stopwatch.ElapsedMilliseconds;

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var totalNodes = index.Count;
        var bytesPerNode = (after - before) / Math.Max(1, totalNodes);

        // 검색 지연 (워밍업 1회 후 측정)
        _ = index.Search("문서", 50);
        stopwatch.Restart();
        var koreanHits = index.Search("문서-document-25", 50);
        var koreanMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var broadHits = index.Search("document", 50);
        var broadMs = stopwatch.ElapsedMilliseconds;

        output.WriteLine($"노드 수: {totalNodes:N0}");
        output.WriteLine($"구축: {buildMs}ms");
        output.WriteLine($"메모리: {(after - before) / 1024 / 1024}MB (노드당 ≈{bytesPerNode}B)");
        output.WriteLine($"검색(좁은 질의): {koreanMs}ms — {koreanHits.Count}건");
        output.WriteLine($"검색(넓은 질의): {broadMs}ms — {broadHits.Count}건");

        totalNodes.Should().BeGreaterThan(190_000);
        koreanHits.Should().NotBeEmpty();
        broadMs.Should().BeLessThan(2000, "20만 항목 전수 검색이 비정상적으로 느리면 안 된다");
    }
}
