namespace Explorer.App.Services;

/// <summary>
/// 버전별 '새 기능' 노트(번들). 업데이트(Velopack) 후 재시작하면 UpdateInfo를 못 읽으므로,
/// GitHub 릴리스 본문이 아니라 앱에 번들된 이 노트를 보여준다. 키는 <see cref="AppInfo.Version"/> 형식("v1.2.0").
/// </summary>
public static class ReleaseNotes
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["v1.3.0"] =
        [
            "탭마다 뒤로/앞으로 기록이 따로 유지됩니다 — 다른 탭의 이동이 섞이지 않습니다.",
            "F2로 이름을 바꿀 때 커서가 파일명 끝에 바로 놓입니다.",
            "정렬 기준 열 머리글에 방향 화살표(▲ 오름차순 · ▼ 내림차순)가 표시됩니다.",
            "환경설정에 '인덱싱 검사 시간'이 생겼습니다 — 매일 그 시각(기본 12:00)에 한 번만 전체 검사해 불필요한 스캔을 줄입니다.",
            "DRM(민감도 레이블) 문서는 노란 자물쇠, 암호화·AIP 문서는 흰색 자물쇠로 구분해 표시합니다.",
            "탐색기·바탕화면에서 파일/폴더를 모든 보기(자세히·간단히·썸네일)로 끌어다 놓을 수 있습니다.",
        ],
        ["v1.2.0"] =
        [
            "보호 파일 자물쇠 정확도 개선 — 암호/AIP·IRM PDF와 레거시 doc·ppt·xls 암호 파일도 🔒로 표시합니다.",
            "Windows 셸 메뉴(우클릭 → Windows 메뉴 표시)에서 바깥을 클릭하면 메뉴가 닫힙니다.",
            "단축키 추가 — 일괄 이름 변경 Ctrl+Shift+R, 폴더 크기 계산 Alt+Shift+S.",
            "상단에 도움말 버튼이 생겼습니다(사용법·단축키 안내).",
            "빠른 실행 바: 맨 앞 + 버튼으로 시작 메뉴에서 추가, 우클릭으로 이름 변경, 검색 결과 우클릭으로 추가.",
            "하단 상태바에 CPU·메모리 사용량을 표시할 수 있습니다(설정에서 켜기).",
            "파일 목록에서 빈 곳을 드래그해 여러 항목을 한 번에 선택할 수 있습니다.",
        ],
    };

    /// <summary>해당 버전의 노트(없으면 null).</summary>
    public static IReadOnlyList<string>? ForVersion(string version) =>
        Notes.TryGetValue(version, out var notes) ? notes : null;
}
