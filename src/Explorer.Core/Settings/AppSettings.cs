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

    /// <summary>네트워크/매핑 드라이브도 인덱싱할지(기본 꺼짐 — USN 불가 + 재귀 스캔이 느리고 무겁다).</summary>
    public bool IndexNetworkDrives { get; init; }

    /// <summary>
    /// 네트워크 인덱싱을 특정 폴더로 한정한다(예: "Z:\\작업", "\\\\server\\share\\folder").
    /// 비우면 네트워크 드라이브 루트 전체를 인덱싱한다(일부 상한). 지정하면 그 폴더들을 상한 없이 전부 인덱싱한다.
    /// </summary>
    public ImmutableArray<string> NetworkIndexFolders { get; init; } = [];

    /// <summary>마지막 세션의 탭/페인 구성 (최초 실행이면 null).</summary>
    public WorkspaceSession? Session { get; init; }

    /// <summary>확장자별 파일명 글자색(점 없는 소문자 → #RRGGBB). 기본 프리셋이 채워져 있다.</summary>
    public ImmutableDictionary<string, string> ExtensionColors { get; init; } = ExtensionColorDefaults.Map;

    /// <summary>파일 목록 보기 모드(자세히/간단히/썸네일).</summary>
    public FileViewMode ViewMode { get; init; } = FileViewMode.Details;

    /// <summary>썸네일 보기의 셀 크기(px). 작게 64 / 보통 96 / 크게 144.</summary>
    public int ThumbnailSize { get; init; } = 96;

    /// <summary>상단 빠른 실행 바를 표시할지.</summary>
    public bool ShowProgramLauncher { get; init; } = true;

    /// <summary>상단 빠른 실행 바에 고정된 프로그램(명령 또는 전체 경로). 기본: 계산기·명령 프롬프트·Edge.</summary>
    public ImmutableArray<string> PinnedPrograms { get; init; } = ["calc.exe", "cmd.exe", "msedge.exe"];
}
