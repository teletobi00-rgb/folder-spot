using System.IO.Enumeration;
using Explorer.Indexing.Index;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing.Sources;

/// <summary>
/// 비권한 폴백 초기 스캔: 루트를 재귀 열거해 인덱스 항목 배치를 생산한다.
/// 접근 불가 디렉터리는 건너뛰며(IgnoreInaccessible), 열거 중 개별 항목의 네트워크/IO 오류는
/// 해당 항목만 건너뛰고 계속한다(<see cref="ResilientIndexEnumerator.ContinueOnError"/>) — 네트워크 드라이브에서
/// 파일 하나의 일시적 오류로 전체 스캔이 중단되지 않게 한다.
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
        CancellationToken cancellationToken = default) =>
        ScanAsync(rootPath, onBatch, long.MaxValue, cancellationToken);

    /// <summary>
    /// 루트를 스캔하되 <paramref name="maxItems"/>에 도달하면 그 자리에서 멈춘다(대용량 네트워크 드라이브 폭주 방지).
    /// </summary>
    public Task<long> ScanAsync(
        string rootPath,
        Action<IReadOnlyList<IndexItem>> onBatch,
        long maxItems,
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

            // 정크 트리(WinSxS·node_modules·.git·캐시 등)는 진입하지도, 포함하지도 않는다 — 노드 수↓ = 메모리↓.
            // ContinueOnError로 네트워크/IO 오류는 해당 항목만 건너뛰고 계속한다(전체 중단 방지).
            using var enumerator = new ResilientIndexEnumerator(rootPath, options);

            long total = 0;
            var batch = new List<IndexItem>(BatchSize);
            while (enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(enumerator.Current);
                total++;
                if (batch.Count >= BatchSize)
                {
                    onBatch(batch);
                    batch = new List<IndexItem>(BatchSize);
                }

                if (total >= maxItems)
                {
                    break;
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

    /// <summary>
    /// 정크 디렉터리는 진입/포함하지 않고, 열거 중 개별 항목 오류(접근 거부·네트워크 IO 등)는 건너뛰고 계속하는 열거자.
    /// 파일은 IsDirectory 단락으로 ToFullPath 비용을 치르지 않는다(디렉터리만 검사).
    /// </summary>
    private sealed class ResilientIndexEnumerator(string directory, EnumerationOptions options)
        : FileSystemEnumerator<IndexItem>(directory, options)
    {
        protected override IndexItem TransformEntry(ref FileSystemEntry entry) => new(
            ParentPath: entry.Directory.ToString(),
            Name: entry.FileName.ToString(),
            IsDirectory: entry.IsDirectory,
            Size: entry.IsDirectory ? 0 : entry.Length,
            ModifiedTicks: entry.LastWriteTimeUtc.LocalDateTime.Ticks);

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) =>
            !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString());

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry) =>
            !entry.IsDirectory
            || !IndexExclusions.IsExcludedDirectory(entry.ToFullPath(), entry.FileName.ToString());

        // 접근 거부·네트워크 일시 오류 등은 해당 항목만 건너뛰고 열거를 계속한다(전체 스캔 중단 방지).
        protected override bool ContinueOnError(int error) => true;
    }
}
