namespace Explorer.Preview.Renderers;

/// <summary>오디오/비디오 — View가 MediaElement로 재생한다.</summary>
public sealed class MediaPreviewRenderer : IPreviewRenderer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v",
        "mp3", "wav", "flac", "m4a", "aac", "ogg", "wma",
    };

    public bool CanRender(string extension) => Extensions.Contains(extension);

    public Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromResult(new PreviewResult
        {
            Kind = PreviewKind.Media,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath),
        });
}
