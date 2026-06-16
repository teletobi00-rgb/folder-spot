using Explorer.Indexing.Index;
using Explorer.Indexing.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Indexing.Tests.Sources;

public sealed class RecursiveScanSourceTests : IDisposable
{
    private readonly string _root;

    public RecursiveScanSourceTests()
    {
        // Temp는 IndexExclusions의 제외 대상이라 스캔이 비게 된다 — 제외되지 않는 테스트 bin 하위에 스크래치 생성.
        _root = Path.Combine(AppContext.BaseDirectory, "ScanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Scan_PopulatesIndexWithFullTree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "한글폴더", "nested"));
        File.WriteAllText(Path.Combine(_root, "root.txt"), "1");
        File.WriteAllText(Path.Combine(_root, "한글폴더", "문서.hwp"), "22");
        File.WriteAllText(Path.Combine(_root, "한글폴더", "nested", "deep.log"), "333");
        using var index = new FileIndex();
        var scanner = new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance);

        var total = await scanner.ScanAsync(_root, index.AddBatch);

        total.Should().Be(5, "폴더 2 + 파일 3");
        index.Search("문서", 10).Should().ContainSingle()
            .Which.FullPath.Should().Be(Path.Combine(_root, "한글폴더", "문서.hwp"));
        index.Search("deep.log", 10).Should().ContainSingle()
            .Which.Size.Should().Be(3);
        index.Search("한글폴더", 10).Should().ContainSingle()
            .Which.IsDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_Cancellation_StopsEnumeration()
    {
        for (var i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "x");
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var scanner = new RecursiveScanSource(NullLogger<RecursiveScanSource>.Instance);

        var act = () => scanner.ScanAsync(_root, _ => { }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
