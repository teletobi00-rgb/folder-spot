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
    /// MIP 민감도 레이블이 적용된(또는 분류 체계를 거친) 비암호화 OOXML 문서 — 노란 자물쇠.
    /// 사내에서 흔히 'DRM 문서'라 부르는 타입. 파일 자체는 열리지만 docMetadata/LabelInfo.xml 레이블 파트를 가진다.
    /// </summary>
    Labeled,
}
