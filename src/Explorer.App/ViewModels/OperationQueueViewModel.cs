using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.Operations;

namespace Explorer.App.ViewModels;

/// <summary>하단 작업 큐 패널. 큐 이벤트는 워커 스레드에서 오므로 UI 컨텍스트로 마샬링한다.</summary>
public sealed partial class OperationQueueViewModel : ObservableObject
{
    private readonly IOperationQueue _queue;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<Guid, OperationItemViewModel> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<OperationItemViewModel> _items = [];

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private int _activeCount;

    public OperationQueueViewModel(IOperationQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        _uiContext = SynchronizationContext.Current;
        _queue.QueueChanged += (_, _) => OnUi(RebuildRows);
        _queue.OperationUpdated += (_, op) => OnUi(() => RefreshRow(op));
    }

    [RelayCommand]
    private void ClearFinished() => _queue.ClearFinished();

    private void OnUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private void RebuildRows()
    {
        var snapshot = _queue.Snapshot;
        var alive = new HashSet<Guid>(snapshot.Select(o => o.Id));
        foreach (var stale in _rows.Keys.Where(id => !alive.Contains(id)).ToArray())
        {
            _rows.Remove(stale);
        }

        foreach (var operation in snapshot)
        {
            if (!_rows.ContainsKey(operation.Id))
            {
                _rows[operation.Id] = new OperationItemViewModel(operation);
            }
        }

        Items = [.. snapshot.Select(o => _rows[o.Id])];
        UpdateAggregates();
    }

    private void RefreshRow(QueuedOperation operation)
    {
        if (_rows.TryGetValue(operation.Id, out var row))
        {
            row.Refresh();
        }

        UpdateAggregates();
    }

    private void UpdateAggregates()
    {
        HasItems = Items.Count > 0;
        ActiveCount = Items.Count(i => !i.IsFinished);
    }
}

/// <summary>큐 항목 한 행 — 상태는 QueuedOperation에서 읽고 Refresh로 전체 재바인딩한다.</summary>
public sealed class OperationItemViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs AllProperties = new(string.Empty);
    private static readonly PropertyChangedEventArgs PercentArgs = new(nameof(Percent));
    private static readonly PropertyChangedEventArgs DetailTextArgs = new(nameof(DetailText));
    private static readonly PropertyChangedEventArgs IsIndeterminateArgs = new(nameof(IsIndeterminate));

    private readonly QueuedOperation _operation;
    private OperationState _lastState;
    private bool _lastPaused;
    private long _lastProgressTick;

    public OperationItemViewModel(QueuedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operation = operation;
        _lastState = operation.State;
        PauseCommand = new RelayCommand(_operation.Control.Pause, () => CanPause);
        ResumeCommand = new RelayCommand(_operation.Control.Resume, () => CanResume);
        CancelCommand = new RelayCommand(_operation.Control.Cancel, () => !IsFinished);
    }

    public string Description => _operation.Description;

    public bool IsFinished => _operation.State
        is OperationState.Completed or OperationState.Failed or OperationState.Canceled;

    public bool IsRunning => _operation.State == OperationState.Running;

    public bool CanPause => IsRunning && !_operation.Control.IsPaused;

    public bool CanResume => IsRunning && _operation.Control.IsPaused;

    public int Percent => _operation.Progress.Percent ?? 0;

    public bool IsIndeterminate => IsRunning && _operation.Progress.Percent is null;

    public string StateText => _operation.State switch
    {
        OperationState.Queued => "대기 중",
        OperationState.Running when _operation.Control.IsPaused => "일시정지",
        OperationState.Running => "진행 중",
        OperationState.Completed => "완료",
        OperationState.Canceled => "취소됨",
        OperationState.Failed => _operation.Result?.Message is { Length: > 0 } message ? $"실패: {message}" : "실패",
        _ => string.Empty,
    };

    public string DetailText
    {
        get
        {
            if (IsFinished)
            {
                return string.Empty;
            }

            var progress = _operation.Progress;
            var parts = new List<string>(3);
            if (progress.ProcessedItems > 0)
            {
                parts.Add($"{progress.ProcessedItems}개 처리");
            }

            if (progress.CurrentItem is { Length: > 0 } current)
            {
                parts.Add(current);
            }

            if (progress.EtaSeconds is { } eta && eta < 24 * 3600)
            {
                parts.Add($"남은 시간 {FormatEta(eta)}");
            }

            return string.Join(" · ", parts);
        }
    }

    public RelayCommand PauseCommand { get; }

    public RelayCommand ResumeCommand { get; }

    public RelayCommand CancelCommand { get; }

    public void Refresh()
    {
        // 상태 전이 때만 전체 리바인딩 — 진행 틱은 빈번하므로(셸 UpdateProgress) 부분 갱신 + 50ms 스로틀.
        var stateChanged = _operation.State != _lastState || _operation.Control.IsPaused != _lastPaused;
        if (stateChanged)
        {
            _lastState = _operation.State;
            _lastPaused = _operation.Control.IsPaused;
            OnPropertyChanged(AllProperties);
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastProgressTick < 50)
        {
            return;
        }

        _lastProgressTick = now;
        OnPropertyChanged(PercentArgs);
        OnPropertyChanged(DetailTextArgs);
        OnPropertyChanged(IsIndeterminateArgs);
    }

    private static string FormatEta(double seconds) => seconds switch
    {
        < 60 => $"{Math.Ceiling(seconds):0}초",
        < 3600 => $"{Math.Ceiling(seconds / 60):0}분",
        _ => $"{seconds / 3600:0.#}시간",
    };
}
