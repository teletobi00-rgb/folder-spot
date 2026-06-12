using Explorer.Core.Search;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Core.Tests.Search;

public sealed class JsonSearchUsageStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public JsonSearchUsageStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "usage.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private JsonSearchUsageStore CreateStore() =>
        new(_filePath, NullLogger<JsonSearchUsageStore>.Instance);

    [Fact]
    public void GetCount_UnknownPath_IsZero()
    {
        CreateStore().GetCount(@"C:\nope.txt").Should().Be(0);
    }

    [Fact]
    public async Task Record_IncrementsAndPersists_CaseInsensitive()
    {
        var store = CreateStore();
        store.Record(@"C:\Docs\a.txt");
        store.Record(@"c:\docs\A.TXT");

        store.GetCount(@"C:\DOCS\a.txt").Should().Be(2);

        // 디스크 기록은 백그라운드 — 반영될 때까지 폴링
        var reloaded = CreateStore();
        var persisted = 0;
        for (var i = 0; i < 100 && persisted != 2; i++)
        {
            await Task.Delay(10);
            reloaded.Load();
            persisted = reloaded.GetCount(@"C:\Docs\a.txt");
        }

        persisted.Should().Be(2);
    }

    [Fact]
    public void Load_CorruptFile_ResetsToEmpty()
    {
        File.WriteAllText(_filePath, "{broken");
        var store = CreateStore();

        store.Load();

        store.GetCount(@"C:\x").Should().Be(0);
    }

    [Fact]
    public void Capacity_KeepsFrequentlyUsed_WhenTrimming()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
        {
            store.Record(@"C:\frequent.txt");
        }

        // 1회짜리 항목을 한도 너머까지 채운다 — 트림은 빈도 낮은 것부터 제거한다.
        for (var i = 0; i < 230; i++)
        {
            store.Record($@"C:\bulk\file{i}.txt");
        }

        store.GetCount(@"C:\frequent.txt").Should().Be(5, "고빈도 항목은 트림에서 살아남는다");
    }
}
