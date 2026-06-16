using Explorer.Shell.Apps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Explorer.Shell.Tests.Apps;

/// <summary>설치된 앱 카탈로그 — 큐레이션 도구는 결정적으로 검증, AppsFolder 열거는 환경 의존이라 출력으로 확인.</summary>
public sealed class InstalledAppCatalogTests
{
    private readonly ITestOutputHelper _output;

    public InstalledAppCatalogTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Search_CuratedConsoleTools_AreAlwaysFound()
    {
        using var catalog = new InstalledAppCatalog(NullLogger<InstalledAppCatalog>.Instance);

        var cmd = await catalog.SearchAsync("cmd", 40);
        var calc = await catalog.SearchAsync("calc", 40);

        cmd.Should().Contain(h => h.App.Name.Contains("cmd", StringComparison.OrdinalIgnoreCase),
            "큐레이션된 명령 프롬프트는 영문 'cmd'로 항상 찾혀야 한다");
        calc.Should().Contain(h => h.App.Name.Contains("calc", StringComparison.OrdinalIgnoreCase)
            || h.App.Name.Contains("계산기", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_EmptyOrWhitespaceQuery_ReturnsEmpty()
    {
        using var catalog = new InstalledAppCatalog(NullLogger<InstalledAppCatalog>.Instance);

        (await catalog.SearchAsync(string.Empty, 40)).Should().BeEmpty();
        (await catalog.SearchAsync("   ", 40)).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_RanksExactAndPrefixAboveSubstring()
    {
        using var catalog = new InstalledAppCatalog(NullLogger<InstalledAppCatalog>.Instance);

        var hits = await catalog.SearchAsync("cmd", 40);

        hits.Should().NotBeEmpty();
        hits.Should().BeInAscendingOrder(h => h.Rank, "정확>접두>부분 순서로 정렬되어야 한다");
    }

    [Fact]
    public async Task Build_EnumeratesAppsFolder_Smoke()
    {
        using var catalog = new InstalledAppCatalog(NullLogger<InstalledAppCatalog>.Instance);

        var word = await catalog.SearchAsync("word", 40);
        var broad = await catalog.SearchAsync("a", 200);

        _output.WriteLine($"'a' 부분일치 앱 수: {broad.Count}");
        foreach (var hit in word.Take(10))
        {
            _output.WriteLine($"word → [rank {hit.Rank}] {hit.App.Name}   ({hit.App.LaunchUri})");
        }

        // AppsFolder 내용은 환경 의존 — 강한 단언 대신 파이프라인이 예외 없이 도는지만 확인.
        broad.Should().NotBeNull();
    }
}
