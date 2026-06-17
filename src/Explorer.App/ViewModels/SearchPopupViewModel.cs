using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Formatting;
using Explorer.Core.Search;
using Explorer.Indexing;
using Explorer.Indexing.Index;
using Explorer.Shell.Apps;
using Explorer.Shell.Icons;
using Microsoft.Extensions.Logging;

namespace Explorer.App.ViewModels;

/// <summary>전역 검색 팝업: as-you-type 디바운스 → 인덱스+설치된 앱 질의 → MRU 가중 랭킹.</summary>
public sealed partial class SearchPopupViewModel : ObservableObject
{
    private const int DebounceMilliseconds = 120;
    private const int MaxResults = 40;

    private readonly FileIndexCatalog _catalog;
    private readonly IFileLauncher _launcher;
    private readonly ISearchUsageStore _usage;
    private readonly IShellIconProvider _icons;
    private readonly IShellThumbnailProvider _thumbnails;
    private readonly IInstalledAppCatalog _apps;
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
        IShellThumbnailProvider thumbnails,
        IInstalledAppCatalog apps,
        ILogger<SearchPopupViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _launcher = launcher;
        _usage = usage;
        _icons = icons;
        _thumbnails = thumbnails;
        _apps = apps;
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

        _usage.Record(selected.UsageKey);
        HideRequested?.Invoke(this, EventArgs.Empty);

        switch (selected.Kind)
        {
            case SearchResultKind.Folder:
                OpenFolderRequested?.Invoke(this, selected.ActivationTarget);
                break;

            case SearchResultKind.App when selected.App is { } app:
                _apps.Launch(app);
                break;

            default:
                LaunchFile(selected.ActivationTarget);
                break;
        }
    }

    private void LaunchFile(string path)
    {
        try
        {
            _launcher.Launch(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "검색 결과 실행 실패: {Path}", path);
        }
    }

    [RelayCommand]
    private void RevealSelected()
    {
        if (Selected is not { } selected || !selected.CanReveal)
        {
            return;
        }

        // 파일/폴더 모두 "들어있는 부모 폴더"를 열고 그 안에서 해당 항목을 선택한다(앱은 제외).
        var directory = PathUtils.GetParent(selected.ActivationTarget) ?? selected.ActivationTarget;

        _usage.Record(selected.UsageKey);
        HideRequested?.Invoke(this, EventArgs.Empty);
        RevealRequested?.Invoke(this, (directory, selected.ActivationTarget));
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
        // 캐시 값을 읽어 UI 스레드가 인덱스 락(쓰기 배치 뒤에서 대기 가능)을 잡지 않게 한다.
        StatusText = $"{_catalog.LastKnownCount:N0}개 항목 인덱싱됨";
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

            IReadOnlyList<SearchHit> fileHits;
            using (var lease = _catalog.Acquire())
            {
                var index = lease.Index;
                fileHits = await Task.Run(() => index.Search(value, MaxResults, ct), ct).ConfigureAwait(true);
            }

            var appHits = await _apps.SearchAsync(value, MaxResults, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var items = new List<SearchResultItemViewModel>(appHits.Count + fileHits.Count);
            foreach (var appHit in appHits)
            {
                items.Add(SearchResultItemViewModel.ForApp(appHit, _thumbnails));
            }

            foreach (var fileHit in fileHits)
            {
                items.Add(SearchResultItemViewModel.ForFile(fileHit, _icons));
            }

            // 인덱스 랭크(정확>접두>부분) 우선, 동순위는 앱 먼저, 그 다음 사용 빈도(MRU) 내림차순.
            Results = [.. items
                .OrderBy(r => r.Rank)
                .ThenByDescending(r => r.IsApp)
                .ThenByDescending(r => _usage.GetCount(r.UsageKey))
                .ThenBy(r => r.Name.Length)
                .Take(MaxResults)];
            Selected = Results.FirstOrDefault();
            IsSearching = false;
        }
        catch (OperationCanceledException)
        {
            // 다음 입력이 이어받는다
        }
    }
}

/// <summary>검색 결과 종류 — 파일/폴더는 인덱스에서, 앱은 설치 프로그램 카탈로그에서 온다.</summary>
public enum SearchResultKind
{
    File,
    Folder,
    App,
}

/// <summary>검색 결과 한 행 — 아이콘은 행이 그려질 때 지연 로드. 파일/폴더/앱 공용.</summary>
public sealed class SearchResultItemViewModel : ObservableObject
{
    private const int AppIconSize = 32;

    private readonly Func<Task<ImageSource?>> _iconLoader;
    private ImageSource? _icon;
    private bool _iconRequested;

    private SearchResultItemViewModel(
        string name,
        string directoryText,
        string detailText,
        int rank,
        SearchResultKind kind,
        string activationTarget,
        string usageKey,
        InstalledApp? app,
        Func<Task<ImageSource?>> iconLoader)
    {
        Name = name;
        DirectoryText = directoryText;
        DetailText = detailText;
        Rank = rank;
        Kind = kind;
        ActivationTarget = activationTarget;
        UsageKey = usageKey;
        App = app;
        _iconLoader = iconLoader;
    }

    public string Name { get; }

    public string DirectoryText { get; }

    public string DetailText { get; }

    public int Rank { get; }

    public SearchResultKind Kind { get; }

    /// <summary>실행/이동 대상 — 파일·폴더는 전체 경로, 앱은 LaunchUri.</summary>
    public string ActivationTarget { get; }

    /// <summary>MRU 가중 키.</summary>
    public string UsageKey { get; }

    /// <summary>앱 결과일 때의 실행 정보(그 외 null).</summary>
    public InstalledApp? App { get; }

    public bool IsApp => Kind == SearchResultKind.App;

    /// <summary>"폴더에서 보기" 가능 여부(앱은 불가).</summary>
    public bool CanReveal => Kind is SearchResultKind.File or SearchResultKind.Folder;

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

    public static SearchResultItemViewModel ForFile(SearchHit hit, IShellIconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(icons);

        var directory = PathUtils.GetParent(hit.FullPath) ?? hit.FullPath;
        var detail = hit.IsDirectory
            ? "폴더"
            : FileSizeFormatter.Format(hit.Size)
                + (hit.Modified == default
                    ? string.Empty
                    : " · " + hit.Modified.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture));

        return new SearchResultItemViewModel(
            hit.Name,
            directory,
            detail,
            hit.Rank,
            hit.IsDirectory ? SearchResultKind.Folder : SearchResultKind.File,
            hit.FullPath,
            hit.FullPath,
            app: null,
            () => icons.GetIconAsync(ToEntry(hit)));
    }

    public static SearchResultItemViewModel ForApp(AppHit hit, IShellThumbnailProvider thumbnails)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(thumbnails);

        var app = hit.App;
        var iconSource = app.IconSource ?? app.LaunchUri;
        return new SearchResultItemViewModel(
            app.Name,
            "앱",
            "프로그램",
            hit.Rank,
            SearchResultKind.App,
            app.LaunchUri,
            "app:" + app.LaunchUri,
            app,
            () => thumbnails.GetThumbnailAsync(iconSource, AppIconSize));
    }

    private static FileEntry ToEntry(SearchHit hit) => FileEntry.Create(
        hit.FullPath,
        hit.Name,
        hit.IsDirectory,
        hit.Size,
        hit.Modified == default ? DateTime.Now : hit.Modified,
        hit.Modified == default ? DateTime.Now : hit.Modified,
        hit.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal);

    private async Task LoadIconAsync()
    {
        var icon = await _iconLoader().ConfigureAwait(true);
        if (icon is not null)
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
        }
    }
}
