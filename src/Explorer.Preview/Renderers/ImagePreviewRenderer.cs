namespace Explorer.Preview.Renderers;

/// <summary>이미지 파일 — 실제 디코드/회전/다운스케일은 View가 BitmapImage로 수행한다.</summary>
public sealed class ImagePreviewRenderer : IPreviewRenderer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff", "jfif",
    };

    public bool CanRender(string extension) => Extensions.Contains(extension);

    public Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromResult(new PreviewResult
        {
            Kind = PreviewKind.Image,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath),
        });
}
