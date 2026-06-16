using System.IO.Enumeration;
using Explorer.Indexing.Index;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing.Sources;

/// <summary>
/// 비권한 폴백 초기 스캔: 루트를 재귀 열거해 인덱스 항목 배치를 생산한다.
/// 접근 불가 디렉터리는 건너뛴다 (IgnoreInaccessible).
/// </summary>
public sealed class RecursiveScanSource
{
    private const int BatchSize = 5000;

    private readonly ILogger<RecursiveScanSource> _logger;

    public RecursiveScanSource(ILogger<RecursiveScanSource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>루트 전체를 스캔해 배치 단위로 전달한다. 스캔한 항목 수를 반환.</summary>
    public Task<long> ScanAsync(
        string rootPath,
        Action<IReadOnlyList<IndexItem>> onBatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(onBatch);

        return Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                RecurseSubdirectories = true,
            };

            var enumerable = new FileSystemEnumerable<IndexItem>(
                rootPath,
                (ref FileSystemEntry entry) => new IndexItem(
                    ParentPath: entry.Directory.ToString(),
                    Name: entry.FileName.ToString(),
                    IsDirectory: entry.IsDirectory,
                    Size: entry.IsDirectory ? 0 : entry.Length,
                    ModifiedTicks: entry.LastWriteTimeUtc.LocalDateTime.Ticks),
                options)
            {
                // 정크 트리(WinSxS·node_modules·.git·캐시 등)는 진입하지도, 포함하지도 않는다 — 노드 수↓ = 메모리↓.
                // 파일은 IsDirectory 단락으로 ToFullPath 비용을 치르지 않는다(디렉터리만 검사).
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString()),
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory
                    || !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString()),
            };

            long total = 0;
            var batch = new List<IndexItem>(BatchSize);
            foreach (var item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(item);
                total++;
                if (batch.Count >= BatchSize)
                {
                    onBatch(batch);
                    batch = new List<IndexItem>(BatchSize);
                }
            }

            if (batch.Count > 0)
            {
                onBatch(batch);
            }

            _logger.LogDebug("재귀 스캔 완료: {Root} — {Count:N0}개 항목", rootPath, total);
            return total;
        }, cancellationToken);
    }
}
