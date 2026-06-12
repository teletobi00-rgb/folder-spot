using Explorer.Indexing.Index;
using Explorer.Indexing.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Indexing.Tests.Sources;

/// <summary>실제 FileSystemWatcher 통합 — 이벤트 전달이 비동기라 폴링으로 검증한다.</summary>
public sealed class FswIndexSourceTests : IDisposable
{
    private readonly string _root;
    private readonly FileIndex _index = new();
    private readonly FswIndexSource _source;
    private readonly List<string> _rescanRequests = [];

    public FswIndexSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _source = new FswIndexSource(
            _index, _rescanRequests.Add, NullLogger<FswIndexSource>.Instance);
        _source.Start(_root);
    }

    public void Dispose()
    {
        _source.Dispose();
        _index.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        condition().Should().BeTrue(because);
    }

    [Fact]
    public async Task CreatedFile_AppearsInIndex_WithFinalSize()
    {
        File.WriteAllText(Path.Combine(_root, "born.txt"), "hello");

        // Created 직후엔 크기 0으로 잡힐 수 있고 Changed가 최종 크기를 반영한다 — 둘 다 기다린다.
        await WaitUntilAsync(
            () => _index.Search("born", 5) is [{ Size: 5 }],
            "생성 이벤트와 최종 크기가 인덱스에 반영");
    }

    [Fact]
    public async Task DeletedFile_DisappearsFromIndex()
    {
        var path = Path.Combine(_root, "doomed.txt");
        File.WriteAllText(path, "x");
        await WaitUntilAsync(() => _index.Search("doomed", 5).Count == 1, "생성 반영");

        File.Delete(path);

        await WaitUntilAsync(() => _index.Search("doomed", 5).Count == 0, "삭제 이벤트가 인덱스에 반영");
    }

    [Fact]
    public async Task RenamedFile_IsReindexedUnderNewName()
    {
        var oldPath = Path.Combine(_root, "old-name.txt");
        File.WriteAllText(oldPath, "x");
        await WaitUntilAsync(() => _index.Search("old-name", 5).Count == 1, "생성 반영");

        File.Move(oldPath, Path.Combine(_root, "new-name.txt"));

        await WaitUntilAsync(() => _index.Search("new-name", 5).Count == 1, "이름변경 반영");
        _index.Search("old-name", 5).Should().BeEmpty();
    }

    [Fact]
    public void WatcherError_RequestsRootRescan()
    {
        _source.OnError(new InvalidOperationException("버퍼 오버플로 시뮬레이션"));

        _rescanRequests.Should().ContainSingle().Which.Should().Be(_root);
    }
}
