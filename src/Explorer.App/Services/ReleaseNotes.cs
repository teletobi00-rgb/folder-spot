namespace Explorer.App.Services;

/// <summary>
/// 버전별 '새 기능' 노트(번들). 업데이트(Velopack) 후 재시작하면 UpdateInfo를 못 읽으므로,
/// GitHub 릴리스 본문이 아니라 앱에 번들된 이 노트를 보여준다. 키는 <see cref="AppInfo.Version"/> 형식("v1.2.0").
/// </summary>
public static class ReleaseNotes
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
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
