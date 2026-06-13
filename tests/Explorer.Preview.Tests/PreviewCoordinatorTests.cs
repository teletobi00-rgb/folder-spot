using Explorer.Preview;
using FluentAssertions;
using NSubstitute;

namespace Explorer.Preview.Tests;

public sealed class PreviewCoordinatorTests
{
    private readonly IPreviewRendererRegistry _registry = Substitute.For<IPreviewRendererRegistry>();

    private PreviewCoordinator CreateCoordinator(TimeSpan? debounce = null) =>
        new(_registry, debounce ?? TimeSpan.FromMilliseconds(20));

    private void SetupRender(string path, PreviewKind kind) =>
        _registry.RenderAsync(path, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PreviewResult { Kind = kind, FilePath = path }));

    private static async Task<T?> WaitForAsync<T>(Func<T?> read, Func<T?, bool> done)
        where T : class
    {
        for (var i = 0; i < 100; i++)
        {
            var value = read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(10);
        }

        return read();
    }

    [Fact]
    public async Task Request_AfterDebounce_RaisesPreviewReady()
    {
        SetupRender(@"C:\a.txt", PreviewKind.Text);
        using var coordinator = CreateCoordinator();
        PreviewResult? received = null;
        coordinator.PreviewReady += (_, r) => received = r;

        coordinator.Request(@"C:\a.txt");

        var result = await WaitForAsync(() => received, r => r is not null);
        result!.Kind.Should().Be(PreviewKind.Text);
    }

    [Fact]
    public async Task RapidRequests_OnlyLastRenders()
    {
        SetupRender(@"C:\a.txt", PreviewKind.Text);
        SetupRender(@"C:\b.png", PreviewKind.Image);
        SetupRender(@"C:\c.mp4", PreviewKind.Media);
        using var coordinator = CreateCoordinator(TimeSpan.FromMilliseconds(40));
        var results = new List<PreviewResult>();
        coordinator.PreviewReady += (_, r) => results.Add(r);

        coordinator.Request(@"C:\a.txt");
        coordinator.Request(@"C:\b.png");
        coordinator.Request(@"C:\c.mp4");

        await WaitForAsync<object>(() => results.Count > 0 ? results : null, r => r is not null);
        await Task.Delay(60);

        results.Should().ContainSingle("디바운스 + 취소로 마지막 요청만 렌더");
        results[0].Kind.Should().Be(PreviewKind.Media);
        await _registry.DidNotReceive().RenderAsync(@"C:\a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_Null_ClearsWithNoneResult()
    {
        using var coordinator = CreateCoordinator();
        PreviewResult? received = null;
        coordinator.PreviewReady += (_, r) => received = r;

        coordinator.Request(null);

        var result = await WaitForAsync(() => received, r => r is not null);
        result!.Kind.Should().Be(PreviewKind.None);
    }

    [Fact]
    public async Task Request_RaisesLoadingTrueThenFalse()
    {
        SetupRender(@"C:\a.txt", PreviewKind.Text);
        using var coordinator = CreateCoordinator();
        var loadingStates = new List<bool>();
        coordinator.LoadingChanged += (_, b) => loadingStates.Add(b);

        coordinator.Request(@"C:\a.txt");

        await WaitForAsync<object>(() => loadingStates.Contains(false) ? loadingStates : null, r => r is not null);
        loadingStates.Should().StartWith(true).And.EndWith(false);
    }
}
