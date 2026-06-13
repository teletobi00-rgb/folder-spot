namespace Explorer.Preview;

/// <summary>
/// 확장자별 미리보기 렌더러. 레지스트리가 first-match로 선택한다.
/// 구현은 예외를 던지지 않고 <see cref="PreviewResult.Error"/>로 보고한다.
/// </summary>
public interface IPreviewRenderer
{
    /// <summary>선행 점 없는 소문자 확장자(예: "png")를 처리할 수 있는지.</summary>
    bool CanRender(string extension);

    Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken);
}
