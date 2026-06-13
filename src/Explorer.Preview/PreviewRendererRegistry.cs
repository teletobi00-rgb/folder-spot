using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace Explorer.Preview;

public interface IPreviewRendererRegistry
{
    /// <summary>경로에 맞는 렌더러를 first-match로 골라 미리보기를 만든다(폴더/없는 파일은 None).</summary>
    Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken);
}

/// <summary>
/// 확장자 기준 first-match 렌더러 선택. 순서가 우선순위이며 마지막은 항상 처리하는 폴백이어야 한다.
/// </summary>
public sealed class PreviewRendererRegistry : IPreviewRendererRegistry
{
    private readonly IReadOnlyList<IPreviewRenderer> _renderers;
    private readonly ILogger<PreviewRendererRegistry> _logger;

    public PreviewRendererRegistry(
        IEnumerable<IPreviewRenderer> renderers,
        ILogger<PreviewRendererRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        ArgumentNullException.ThrowIfNull(logger);
        _renderers = [.. renderers];
        _logger = logger;
    }

    public async Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return PreviewResult.None(filePath ?? string.Empty);
        }

        var extension = GetExtension(filePath);
        foreach (var renderer in _renderers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!renderer.CanRender(extension))
            {
                continue;
            }

            try
            {
                return await renderer.RenderAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 렌더러가 계약을 어기고 예외를 던져도 미리보기 전체가 죽지 않도록 격리한다.
                _logger.LogWarning(ex, "렌더러 {Renderer} 실패 — 다음 렌더러로: {Path}", renderer.GetType().Name, filePath);
            }
        }

        return PreviewResult.Error(filePath, "미리보기를 지원하지 않는 형식입니다.");
    }

    private static string GetExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.Length > 1 ? ext[1..].ToLowerInvariant() : string.Empty;
    }
}
