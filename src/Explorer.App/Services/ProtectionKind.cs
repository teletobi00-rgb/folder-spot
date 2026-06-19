namespace Explorer.App.Services;

/// <summary>
/// 파일 보호 종류 — 자물쇠 아이콘 색을 결정한다.
/// </summary>
public enum ProtectionKind
{
    /// <summary>보호 없음.</summary>
    None,

    /// <summary>암호화/AIP·IRM·표준 암호로 실제 암호화된 파일 — 흰색 자물쇠.</summary>
    Encrypted,

    /// <summary>
    /// 사내 DRM(SoftCamp Document Security)으로 보호·암호화된 파일 — 노란 자물쇠.
    /// 파일 전체가 벤더 컨테이너로 암호화되며 매직 헤더 "SCDS"로 식별한다.
    /// (AIP 민감도 레이블만 붙은 일반 문서는 보호로 보지 않는다 — 정상적으로 열리므로 None.)
    /// </summary>
    Drm,
}
