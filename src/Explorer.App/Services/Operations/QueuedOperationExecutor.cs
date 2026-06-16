using System.Diagnostics;
using System.IO;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Operations;
using Explorer.Core.Undo;
using Microsoft.Extensions.Logging;

namespace Explorer.App.Services.Operations;

/// <summary>
/// 큐 작업 한 건의 전체 흐름: 충돌 사전 검사 → 사용자 결정 → 결정 그룹별 실행(진행/일시정지/취소) → Undo 기록.
/// 큐 워커 스레드에서 실행된다.
/// </summary>
public sealed class QueuedOperationExecutor : IQueuedOperationExecutor
{
    private readonly IFileOperationService _operations;
    private readonly IConflictPrompt _conflictPrompt;
    private readonly IUndoService _undo;
    private readonly ILogger<QueuedOperationExecutor> _logger;

    public QueuedOperationExecutor(
        IFileOperationService operations,
        IConflictPrompt conflictPrompt,
        IUndoService undo,
        ILogger<QueuedOperationExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(conflictPrompt);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(logger);
        _operations = operations;
        _conflictPrompt = conflictPrompt;
        _undo = undo;
        _logger = logger;
    }

    public async Task<FileOperationResult> ExecuteAsync(QueuedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var request = operation.Request;

        return request.Kind switch
        {
            OperationKind.Copy or OperationKind.Move => await TransferAsync(operation).ConfigureAwait(false),
            OperationKind.Delete or OperationKind.DeletePermanent => await DeleteAsync(operation).ConfigureAwait(false),
            _ => FileOperationResult.Failure(FileOperationError.Unknown, "지원하지 않는 작업입니다."),
        };
    }

    private async Task<FileOperationResult> TransferAsync(QueuedOperation operation)
    {
        var request = operation.Request;
        if (request.Destination is not { } destination)
        {
            return FileOperationResult.Failure(FileOperationError.PathNotFound, "대상 폴더가 없습니다.");
        }

        // 1) 충돌 사전 검사 + 사용자 결정
        var conflicts = ConflictScanner.Scan(request.Sources, destination);
        var conflictSources = conflicts.Select(c => c.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keepBoth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var overwrite = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (conflicts.Count > 0)
        {
            var decisions = await _conflictPrompt.ResolveAsync(conflicts).ConfigureAwait(false);
            if (decisions is null)
            {
                return FileOperationResult.Cancelled();
            }

            foreach (var conflict in conflicts)
            {
                if (!decisions.TryGetValue(conflict, out var decision))
                {
                    return FileOperationResult.Cancelled();
                }

                switch (decision)
                {
                    case ConflictDecision.Overwrite:
                        overwrite.Add(conflict.SourcePath);
                        break;
                    case ConflictDecision.Skip:
                        skipped.Add(conflict.SourcePath);
                        break;
                    case ConflictDecision.KeepBoth:
                        keepBoth.Add(conflict.SourcePath);
                        break;
                }
            }
        }

        // 2) 결정 그룹: 무충돌 / 명시적 덮어쓰기 / 둘 다 유지. 건너뛰기는 제외.
        var defaultGroup = request.Sources
            .Where(s => !conflictSources.Contains(s) && !skipped.Contains(s))
            .ToArray();
        var overwriteGroup = request.Sources
            .Where(overwrite.Contains)
            .ToArray();
        var keepBothGroup = keepBoth.ToArray();

        if (defaultGroup.Length == 0 && overwriteGroup.Length == 0 && keepBothGroup.Length == 0)
        {
            return FileOperationResult.Success(); // 전부 건너뜀
        }

        // 3) 실행 (진행은 그룹 가중 평균으로 합산)
        var move = request.Kind == OperationKind.Move;
        var completed = new List<CompletedItem>();
        var totalItems = defaultGroup.Length + overwriteGroup.Length + keepBothGroup.Length;
        var groups = new List<(string[] Sources, CollisionOption Collision)>();
        if (defaultGroup.Length > 0)
        {
            groups.Add((defaultGroup, CollisionOption.Default));
        }

        if (overwriteGroup.Length > 0)
        {
            groups.Add((overwriteGroup, CollisionOption.Overwrite));
        }

        if (keepBothGroup.Length > 0)
        {
            groups.Add((keepBothGroup, CollisionOption.KeepBoth));
        }

        var stopwatch = Stopwatch.StartNew();
        var estimator = new ProgressEstimator();
        var doneItems = 0;

        foreach (var (sources, collision) in groups)
        {
            var groupWeight = (double)sources.Length / totalItems;
            var baseProgress = (double)doneItems / totalItems * 100;
            var events = new ExecutorEvents(operation, completed, estimator, stopwatch,
                percent => baseProgress + (percent * groupWeight));

            var context = new FileOperationContext
            {
                Collision = collision,
                Control = operation.Control,
                Events = events,
            };

            var result = move
                ? await _operations.MoveAsync(sources, destination, context).ConfigureAwait(false)
                : await _operations.CopyAsync(sources, destination, context).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return result;
            }

            doneItems += sources.Length;
        }

        PushTransferUndo(move, completed);
        return FileOperationResult.Success();
    }

    private async Task<FileOperationResult> DeleteAsync(QueuedOperation operation)
    {
        var request = operation.Request;
        var permanent = request.Kind == OperationKind.DeletePermanent;
        var completed = new List<CompletedItem>();
        var events = new ExecutorEvents(
            operation, completed, new ProgressEstimator(), Stopwatch.StartNew(), percent => percent);

        var context = new FileOperationContext
        {
            Control = operation.Control,
            Events = events,
        };

        var result = await _operations.DeleteAsync(request.Sources, permanent, context).ConfigureAwait(false);
        if (result.Succeeded && !permanent)
        {
            PushRecycleUndo(completed);
        }

        return result;
    }

    /// <summary>복사 → 생성본 삭제 / 이동 → 되돌려 이동.</summary>
    private void PushTransferUndo(bool move, IReadOnlyList<CompletedItem> completed)
    {
        var items = completed.Where(c => c.NewPath is not null).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        if (move)
        {
            _undo.Push(new UndoEntry($"{items.Length}개 항목 이동", async () =>
            {
                foreach (var item in items)
                {
                    var originalDir = PathUtils.GetParent(item.SourcePath);
                    var originalName = Path.GetFileName(item.SourcePath.TrimEnd(Path.DirectorySeparatorChar));
                    if (originalDir is null)
                    {
                        continue;
                    }

                    var result = await _operations
                        .MoveItemAsync(item.NewPath!, originalDir, originalName).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        return result;
                    }
                }

                return FileOperationResult.Success();
            }));
            return;
        }

        var createdPaths = items.Select(i => i.NewPath!).ToArray();
        _undo.Push(new UndoEntry(
            $"{createdPaths.Length}개 항목 복사",
            () => _operations.DeleteAsync(createdPaths, permanent: false)));
    }

    /// <summary>
    /// 휴지통 삭제 → 휴지통의 물리 항목($R...)을 원래 위치/이름으로 되돌려 이동.
    /// 알려진 트레이드오프: 짝이 되는 $I 메타데이터가 남아 휴지통 UI에 유령 항목이 보일 수 있다
    /// (휴지통 비우기 시 정리됨). 휴지통 미사용 볼륨에서는 $R 경로가 보고되지 않아 Undo가 등록되지 않는다.
    /// </summary>
    private void PushRecycleUndo(IReadOnlyList<CompletedItem> completed)
    {
        var items = completed.Where(c => c.NewPath is not null).ToArray();
        if (items.Length == 0)
        {
            _logger.LogDebug("휴지통 항목 경로를 얻지 못해 삭제 Undo를 기록하지 않습니다.");
            return;
        }

        _undo.Push(new UndoEntry($"{items.Length}개 항목 삭제", async () =>
        {
            foreach (var item in items)
            {
                var originalDir = PathUtils.GetParent(item.SourcePath);
                var originalName = Path.GetFileName(item.SourcePath.TrimEnd(Path.DirectorySeparatorChar));
                if (originalDir is null)
                {
                    continue;
                }

                var result = await _operations
                    .MoveItemAsync(item.NewPath!, originalDir, originalName).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return FileOperationResult.Success();
        }));
    }

    /// <summary>셸 이벤트(작업 STA 스레드) → 진행 스냅샷/완료 항목 수집.</summary>
    private sealed class ExecutorEvents : IOperationEvents
    {
        private readonly QueuedOperation _operation;
        private readonly List<CompletedItem> _completed;
        private readonly ProgressEstimator _estimator;
        private readonly Stopwatch _stopwatch;
        private readonly Func<int, double> _mapPercent;
        private int _processedItems;

        public ExecutorEvents(
            QueuedOperation operation,
            List<CompletedItem> completed,
            ProgressEstimator estimator,
            Stopwatch stopwatch,
            Func<int, double> mapPercent)
        {
            _operation = operation;
            _completed = completed;
            _estimator = estimator;
            _stopwatch = stopwatch;
            _mapPercent = mapPercent;
        }

        public void OnProgress(int percent)
        {
            var overall = Math.Clamp(_mapPercent(percent), 0, 100);
            var elapsed = _stopwatch.Elapsed.TotalSeconds;
            _estimator.AddSample(elapsed, overall);

            _operation.ReportProgress(new OperationProgress
            {
                Percent = (int)overall,
                ProcessedItems = _processedItems,
                EtaSeconds = _estimator.GetEtaSeconds(overall),
                PercentPerSecond = _estimator.GetPercentPerSecond(),
            });
        }

        public void OnItemCompleted(CompletedItem item)
        {
            lock (_completed)
            {
                _completed.Add(item);
                _processedItems = _completed.Count;
            }

            _operation.ReportProgress(_operation.Progress with
            {
                ProcessedItems = _processedItems,
                CurrentItem = Path.GetFileName(item.SourcePath.TrimEnd(Path.DirectorySeparatorChar)),
            });
        }
    }
}
