using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Explorer.Preview.Renderers;

/// <summary>압축 파일 — SharpCompress로 항목 목록을 나열한다(추출은 하지 않음).</summary>
public sealed class ArchivePreviewRenderer : IPreviewRenderer
{
    private const int MaxEntries = 2000;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz",
    };

    private readonly ILogger<ArchivePreviewRenderer> _logger;

    public ArchivePreviewRenderer(ILogger<ArchivePreviewRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public bool CanRender(string extension) => Extensions.Contains(extension);

    public Task<PreviewResult> RenderAsync(string filePath, CancellationToken cancellationToken) =>
        Task.Run(() => ListEntries(filePath, cancellationToken), cancellationToken);

    private PreviewResult ListEntries(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ArchiveFactory.Open(filePath);
            var entries = ImmutableArray.CreateBuilder<ArchiveEntryInfo>();
            var truncated = false;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= MaxEntries)
                {
                    truncated = true;
                    break;
                }

                entries.Add(new ArchiveEntryInfo(
                    Path: entry.Key ?? "(이름 없음)",
                    Size: entry.Size,
                    IsDirectory: entry.IsDirectory));
            }

            return new PreviewResult
            {
                Kind = PreviewKind.Archive,
                FilePath = filePath,
                DisplayName = Path.GetFileName(filePath),
                ArchiveEntries = entries.ToImmutable(),
                ArchiveTruncated = truncated,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SharpCompress는 다양한 예외(손상/암호/미지원)를 던진다 — 모두 오류로 보고.
            _logger.LogDebug(ex, "압축 미리보기 실패: {Path}", filePath);
            return PreviewResult.Error(filePath, $"압축 파일을 열 수 없습니다: {ex.Message}");
        }
    }
}
