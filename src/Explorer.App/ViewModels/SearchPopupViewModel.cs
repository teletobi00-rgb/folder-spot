using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Formatting;
using Explorer.Core.Search;
using Explorer.Indexing;
using Explorer.Indexing.Index;
using Explorer.Shell.Icons;
using Microsoft.Extensions.Logging;

namespace Explorer.App.ViewModels;

/// <summary>전역 검색 팝업: as-you-type 디바운스 → 인덱스 질의 → MRU 가중 랭킹.</summary>
public sealed partial class SearchPopupViewModel : ObservableObject
{
    private const int DebounceMilliseconds = 120;
    private const int MaxResults = 40;

    private readonly FileIndexCatalog _catalog;
    private readonly IFileLauncher _launcher;
    private readonly ISearchUsageStore _usage;
    private readonly IShellIconProvider _icons;
    private readonly ILogger<SearchPopupViewModel> _logger;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SearchResultItemViewModel> _results = [];

    [ObservableProperty]
    private SearchResultItemViewModel? _selected;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public SearchPopupViewModel(
        FileIndexCatalog catalog,
        IFileLauncher launcher,
        ISearchUsageStore usage,
        IShellIconProvider icons,
        ILogger<SearchPopupViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _launcher = launcher;
        _usage = usage;
        _icons = icons;
        _logger = logger;
    }

    /// <summary>결과 실행/이동 후 팝업을 닫아달라는 신호.</summary>
    public event EventHandler? HideRequested;

    /// <summary>폴더 결과 열기 — 메인 창 활성 페인이 이 경로로 이동한다.</summary>
    public event EventHandler<string>? OpenFolderRequested;

    /// <summary>Ctrl+Enter — 항목이 들어있는 폴더를 열고 그 항목을 선택한다.</summary>
    public event EventHandler<(string Directory, string FullPath)>? RevealRequested;

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is not { } selected)
        {
            return;
        }

        _usage.Record(selected.FullPath);
        HideRequested?.Invoke(this, EventArgs.Empty);

        if (selected.IsDirectory)
        {
            OpenFolderRequested?.Invoke(this, selected.FullPath);
            return;
        }

        try
        {
            _launcher.Launch(selected.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "검색 결과 실행 실패: {Path}", selected.FullPath);
        }
    }

    [RelayCommand]
    private void RevealSelected()
    {
        if (Selected is not { } selected)
        {
            return;
        }

        // 파일/폴더 모두 "들어있는 부모 폴더"를 열고 그 안에서 해당 항목을 선택한다.
        var directory = PathUtils.GetParent(selected.FullPath) ?? selected.FullPath;

        _usage.Record(selected.FullPath);
        HideRequested?.Invoke(this, EventArgs.Empty);
        RevealRequested?.Invoke(this, (directory, selected.FullPath));
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < Results.Count; i++)
        {
            if (ReferenceEquals(Results[i], Selected))
            {
                index = i;
                break;
            }
        }

        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    /// <summary>팝업이 표시될 때 호출 — 직전 질의를 다시 보여주되 전체 선택 상태로.</summary>
    public void OnShown()
    {
        StatusText = $"{_catalog.Current.Count:N0}개 항목 인덱싱됨";
    }

    async partial void OnQueryChanged(string value)
    {
        // 이전 호출이 아직 자기 토큰을 들고 있는 동안 Dispose하지 않도록 교체 후 취소한다.
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, cts);
        previous?.Cancel();
        var ct = cts.Token;

        if (string.IsNullOrWhiteSpace(value))
        {
            Results = [];
            Selected = null;
            IsSearching = false;
            return;
        }

        IsSearching = true;
        try
        {
            await Task.Delay(DebounceMilliseconds, ct).ConfigureAwait(true);

            var index = _catalog.Current;
            var hits = await Task.Run(() => index.Search(value, MaxResults, ct), ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            // MRU 가중: 인덱스 랭크(정확>접두>부분) 우선, 동순위에서 사용 빈도 내림차순.
            Results = [.. hits
                .OrderBy(h => h.Rank)
                .ThenByDescending(h => _usage.GetCount(h.FullPath))
                .ThenBy(h => h.Name.Length)
                .Select(h => new SearchResultItemViewModel(h, _icons))];
            Selected = Results.FirstOrDefault();
            IsSearching = false;
        }
        catch (OperationCanceledException)
        {
            // 다음 입력이 이어받는다
        }
    }
}

/// <summary>검색 결과 한 행 — 아이콘은 행이 그려질 때 지연 로드.</summary>
public sealed class SearchResultItemViewModel : ObservableObject
{
    private readonly IShellIconProvider _icons;
    private ImageSource? _icon;
    private bool _iconRequested;

    public SearchResultItemViewModel(SearchHit hit, IShellIconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(icons);
        _icons = icons;
        Hit = hit;
        DirectoryText = PathUtils.GetParent(hit.FullPath) ?? hit.FullPath;
        DetailText = hit.IsDirectory
            ? "폴더"
            : FileSizeFormatter.Format(hit.Size)
                + (hit.Modified == default
                    ? string.Empty
                    : " · " + hit.Modified.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture));
    }

    public SearchHit Hit { get; }

    public string Name => Hit.Name;

    public string FullPath => Hit.FullPath;

    public bool IsDirectory => Hit.IsDirectory;

    public string DirectoryText { get; }

    public string DetailText { get; }

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }

            return _icon;
        }
    }

    private async Task LoadIconAsync()
    {
        var entry = Explorer.Core.FileSystem.FileEntry.Create(
            FullPath, Name, IsDirectory, Hit.Size,
            Hit.Modified == default ? DateTime.Now : Hit.Modified,
            Hit.Modified == default ? DateTime.Now : Hit.Modified,
            IsDirectory ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal);
        var icon = await _icons.GetIconAsync(entry).ConfigureAwait(true);
        if (icon is not null)
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
        }
    }
}
