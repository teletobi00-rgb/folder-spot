using System.Collections.Immutable;

namespace Explorer.Shell.Apps;

/// <summary>설치된 프로그램/앱 한 건 (전역 검색에 노출).</summary>
public sealed record InstalledApp
{
    public required string Name { get; init; }

    /// <summary>실행 대상 — "shell:AppsFolder\AUMID"(UWP/데스크톱 앱) 또는 실행 파일 경로/명령.</summary>
    public required string LaunchUri { get; init; }

    /// <summary>아이콘 로드용 경로/URI(없으면 LaunchUri 사용).</summary>
    public string? IconSource { get; init; }

    /// <summary>매칭용 소문자 키워드(이름 토큰 + 실행 파일명 + 별칭).</summary>
    public ImmutableArray<string> Keywords { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>앱 검색 결과 한 건(랭크: 0=정확, 1=접두, 2=부분 — 파일 검색과 동일 척도).</summary>
public sealed record AppHit(InstalledApp App, int Rank);
