using System.Collections.Immutable;
using Explorer.Core.Workspace;

namespace Explorer.Core.Settings;

/// <summary>앱 전역 설정 스냅샷. 변경은 항상 with 식으로 새 인스턴스를 만든다.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppTheme Theme { get; init; } = AppTheme.System;

    public bool ShowHiddenFiles { get; init; }

    /// <summary>
    /// NTFS 고속 인덱싱(MFT/USN) 사용 여부. 권한 헬퍼를 UAC로 띄우므로 기본은 꺼짐(옵트인).
    /// 꺼져 있으면 권한 불필요한 재귀+FSW 폴백으로 완전 동작한다.
    /// </summary>
    public bool UseFastIndexing { get; init; }

    /// <summary>마지막 세션의 탭/페인 구성 (최초 실행이면 null).</summary>
    public WorkspaceSession? Session { get; init; }

    /// <summary>확장자별 파일명 글자색(점 없는 소문자 → #RRGGBB). 기본 프리셋이 채워져 있다.</summary>
    public ImmutableDictionary<string, string> ExtensionColors { get; init; } = ExtensionColorDefaults.Map;
}
