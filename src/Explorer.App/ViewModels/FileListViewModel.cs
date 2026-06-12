using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Navigation;
using Explorer.Core.Settings;
using Explorer.Core.Sorting;
using Explorer.Shell.Icons;
using Microsoft.Extensions.Logging;

namespace Explorer.App.ViewModels;

public enum FileListStatus
{
    None,
    Empty,
    NotFound,
    AccessDenied,
    Error,
}

public sealed partial class FileListViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemEnumerator _enumerator;
    private readonly IFileLauncher _launcher;
    private readonly ISettingsService _settings;
    private readonly IShellIconProvider _iconProvider;
    private readonly ILogger<FileListViewModel> _logger;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string? _currentPath;

    [ObservableProperty]
    private IReadOnlyList<FileItemViewModel> _items = [];

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private FileListStatus _status = FileListStatus.None;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private SortDescriptor _sort = SortDescriptor.Default;

    public FileListViewModel(
        IFileSystemEnumerator enumerator,
        IFileLauncher launcher,
        ISettingsService settings,
        IShellIconProvider iconProvider,
        ILogger<FileListViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(iconProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _enumerator = enumerator;
        _launcher = launcher;
        _settings = settings;
        _iconProvider = iconProvider;
        _logger = logger;
    }

    public NavigationHistory History { get; private set; } = NavigationHistory.Empty;

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    [RelayCommand]
    public async Task NavigateToAsync(string path)
    {
        string normalized;
        try
        {
            normalized = PathUtils.Normalize(path);
        }
        catch (ArgumentException ex)
        {
            Status = FileListStatus.Error;
            StatusMessage = ex.Message;
            return;
        }

        History = History.Visit(normalized);
        CurrentPath = normalized;
        NotifyNavigationCommands();
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBackAsync()
    {
        History = History.GoBack();
        CurrentPath = History.Current;
        NotifyNavigationCommands();
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task GoForwardAsync()
    {
        History = History.GoForward();
        CurrentPath = History.Current;
        NotifyNavigationCommands();
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task GoUpAsync()
    {
        var parent = CurrentPath is null ? null : PathUtils.GetParent(CurrentPath);
        if (parent is not null)
        {
            await NavigateToAsync(parent);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentPath is not null)
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task OpenItemAsync(FileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            await NavigateToAsync(item.Entry.FullPath);
            return;
        }

        try
        {
            _launcher.Launch(item.Entry.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "파일 실행 실패: {Path}", item.Entry.FullPath);
            StatusMessage = $"파일을 열 수 없습니다: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChangeSortAsync(SortColumn column)
    {
        Sort = Sort.Toggle(column);

        // 재나열 없이 기존 항목(로드된 아이콘 포함)을 그대로 재정렬한다.
        var current = Items;
        var comparer = FileEntryComparers.Create(Sort);
        var sorted = await Task.Run(() =>
        {
            var list = current.ToList();
            list.Sort((a, b) => comparer.Compare(a.Entry, b.Entry));
            return list;
        }).ConfigureAwait(true);

        Items = sorted;
    }

    private bool CanGoBack() => History.CanGoBack;

    private bool CanGoForward() => History.CanGoForward;

    private bool CanGoUp() => CurrentPath is not null && PathUtils.GetParent(CurrentPath) is not null;

    private void NotifyNavigationCommands()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    // 모든 명령과 함께 UI 스레드에서만 호출되어야 한다(_loadCts 접근과 ObservableProperty 갱신이 이를 전제).
    // 내부 await는 ConfigureAwait(true)로 UI 컨텍스트를 유지한다.
    private async Task ReloadAsync()
    {
        var path = CurrentPath;
        if (path is null)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        Status = FileListStatus.None;
        StatusMessage = null;

        try
        {
            var entries = await _enumerator.ListAsync(path, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var showHidden = _settings.Current.ShowHiddenFiles;
            var comparer = FileEntryComparers.Create(Sort);
            var items = await Task.Run(
                () =>
                {
                    var filtered = showHidden ? entries.ToList() : entries.Where(e => !e.IsHidden).ToList();
                    filtered.Sort(comparer);
                    return filtered.Select(e => new FileItemViewModel(e, _iconProvider)).ToArray();
                },
                ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            Items = items;
            SelectedItem = null;
            Status = items.Length == 0 ? FileListStatus.Empty : FileListStatus.None;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            Items = [];
            Status = FileListStatus.NotFound;
        }
        catch (UnauthorizedAccessException)
        {
            Items = [];
            Status = FileListStatus.AccessDenied;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            _logger.LogError(ex, "폴더 로드 실패: {Path}", path);
            Items = [];
            Status = FileListStatus.Error;
            StatusMessage = ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }
}
