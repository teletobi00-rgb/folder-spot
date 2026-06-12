using System.Diagnostics;
using Explorer.Core.FileSystem;
using Explorer.Core.Sorting;
using FluentAssertions;
using Xunit.Abstractions;

namespace Explorer.Core.Tests.Perf;

/// <summary>대용량 실폴더(WinSxS) 나열+정렬 시간 스모크. 폴더가 없으면 건너뛴다.</summary>
public sealed class EnumeratorPerfSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ListAndSort_LargeRealDirectory_CompletesAndReportsTiming()
    {
        var winSxS = @"C:\Windows\WinSxS";
        if (!Directory.Exists(winSxS))
        {
            output.WriteLine("WinSxS 없음 — 건너뜀");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var entries = await new FileSystemEnumerator().ListAsync(winSxS);
        var listMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var sorted = entries.ToList();
        sorted.Sort(FileEntryComparers.Create(SortDescriptor.Default));
        var sortMs = stopwatch.ElapsedMilliseconds;

        output.WriteLine($"항목 수: {entries.Count:N0}, 나열: {listMs}ms, 정렬: {sortMs}ms");
        entries.Should().NotBeEmpty();
    }
}
