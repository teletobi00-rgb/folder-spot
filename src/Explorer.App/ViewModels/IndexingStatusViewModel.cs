using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.Indexing;

namespace Explorer.App.ViewModels;

/// <summary>
/// 인덱싱 진행 상태를 UI에 노출한다. IndexingService의 백그라운드 이벤트를 UI Dispatcher로 마샬링해
/// 드라이브별 단계/항목 수를 요약(<see cref="Summary"/>)·상세(<see cref="Detail"/>)로 제공한다.
/// </summary>
public sealed partial class IndexingStatusViewModel : ObservableObject, IIndexingStatus, IDisposable
{
    private readonly IndexingService _indexing;
    private readonly FileIndexCatalog _catalog;
    private readonly Dictionary<string, DriveIndexProgress> _states = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _summary = "인덱스 준비 중…";

    [ObservableProperty]
    private string _detail = string.Empty;

    /// <summary>현재 스캔/대기 중인 드라이브가 있으면 true(스피너 표시용).</summary>
    [ObservableProperty]
    private bool _isIndexing;

    /// <summary>인덱싱 대상이 하나라도 있으면 true(상태바 표시 여부).</summary>
    [ObservableProperty]
    private bool _hasActivity;

    public IndexingStatusViewModel(IndexingService indexing, FileIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(indexing);
        ArgumentNullException.ThrowIfNull(catalog);
        _indexing = indexing;
        _catalog = catalog;

        foreach (var progress in _indexing.DriveProgress)
        {
            _states[progress.Root] = progress;
        }

        Recompute();
        _indexing.DriveProgressChanged += OnDriveProgressChanged;
    }

    private void OnDriveProgressChanged(object? sender, DriveIndexProgress e)
    {
        // 이벤트는 인덱싱 백그라운드 스레드에서 온다 — UI 스레드로 마샬링한다.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply(e);
        }
        else
        {
            dispatcher.BeginInvoke(() => Apply(e));
        }
    }

    private void Apply(DriveIndexProgress e)
    {
        _states[e.Root] = e;
        Recompute();
    }

    private void Recompute()
    {
        if (_states.Count == 0)
        {
            Summary = "인덱싱 대상 없음";
            Detail = string.Empty;
            IsIndexing = false;
            HasActivity = false;
            return;
        }

        HasActivity = true;
        var active = _states.Values
            .Where(s => s.Phase is DriveIndexPhase.Scanning or DriveIndexPhase.Pending)
            .Select(s => Label(s.Root))
            .ToList();
        IsIndexing = active.Count > 0;

        Summary = IsIndexing
            ? $"인덱싱 중… {string.Join(", ", active)}"
            : $"인덱스 감시 중 · {_catalog.LastKnownCount:N0}개";

        Detail = string.Join(
            Environment.NewLine,
            _states.Values
                .OrderBy(s => s.Root, StringComparer.OrdinalIgnoreCase)
                .Select(s => $"{Label(s.Root)}  —  {PhaseText(s.Phase)}{(s.ItemCount > 0 ? $" ({s.ItemCount:N0}개)" : string.Empty)}"));
    }

    private static string Label(string root) => root.TrimEnd('\\', '/');

    private static string PhaseText(DriveIndexPhase phase) => phase switch
    {
        DriveIndexPhase.Pending => "대기",
        DriveIndexPhase.Scanning => "스캔 중",
        DriveIndexPhase.Watching => "감시 중",
        DriveIndexPhase.Skipped => "최신(생략)",
        DriveIndexPhase.Partial => "일부만(상한 도달)",
        DriveIndexPhase.Error => "실패",
        _ => string.Empty,
    };

    public void Dispose() => _indexing.DriveProgressChanged -= OnDriveProgressChanged;
}
