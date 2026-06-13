using System.Collections.Immutable;
using System.Globalization;
using Explorer.Core.Formatting;

namespace Explorer.Preview.Renderers;

/// <summary>폴백 — 모든 확장자를 처리한다. 파일 기본 정보(크기/날짜/속성)를 표시한다.</summary>
public sealed class InfoPreviewRenderer : IPreviewRenderer
{
    public bool CanRender(string extension) => true;

    public Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = ImmutableArray.CreateBuilder<InfoLine>();
        try
        {
            var info = new FileInfo(filePath);
            lines.Add(new InfoLine("종류", DescribeExtension(filePath)));
            lines.Add(new InfoLine("크기", $"{FileSizeFormatter.Format(info.Length)} ({info.Length:N0} 바이트)"));
            lines.Add(new InfoLine("수정한 날짜", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)));
            lines.Add(new InfoLine("만든 날짜", info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)));
            lines.Add(new InfoLine("속성", info.Attributes.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(PreviewResult.Error(filePath, $"파일 정보를 읽을 수 없습니다: {ex.Message}"));
        }

        return Task.FromResult(new PreviewResult
        {
            Kind = PreviewKind.Info,
            FilePath = filePath,
            DisplayName = Path.GetFileName(filePath),
            InfoLines = lines.ToImmutable(),
        });
    }

    private static string DescribeExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(ext) ? "파일" : ext.TrimStart('.').ToUpperInvariant() + " 파일";
    }
}
