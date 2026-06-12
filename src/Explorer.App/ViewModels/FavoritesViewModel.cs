using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace Explorer.App.ViewModels;

/// <summary>사이드바 즐겨찾기 섹션. 폴더 항목은 활성 페인 탐색, 파일 항목은 실행.</summary>
public sealed partial class FavoritesViewModel : ObservableObject
{
    private readonly IFavoritesService _favorites;
    private readonly IFileLauncher _launcher;
    private readonly ILogger<FavoritesViewModel> _logger;

    [ObservableProperty]
    private IReadOnlyList<FavoriteItemViewModel> _items = [];

    public FavoritesViewModel(
        IFavoritesService favorites,
        IFileLauncher launcher,
        ILogger<FavoritesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);
        _favorites = favorites;
        _launcher = launcher;
        _logger = logger;
        _favorites.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>폴더 즐겨찾기 클릭 시 발생 — 활성 페인이 이 경로로 이동한다.</summary>
    public event EventHandler<string>? FolderOpenRequested;

    /// <summary>시작 시 1회: 디스크에서 즐겨찾기를 읽는다.</summary>
    public void Initialize() => _favorites.Load();

    /// <summary>드래그앤드롭으로 추가 (잘못된 경로는 건너뜀).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            try
            {
                _favorites.Add(path);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "즐겨찾기 추가 실패: {Path}", path);
            }
        }
    }

    [RelayCommand]
    private void Open(FavoriteItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            FolderOpenRequested?.Invoke(this, item.Path);
            return;
        }

        try
        {
            _launcher.Launch(item.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "즐겨찾기 실행 실패: {Path}", item.Path);
        }
    }

    [RelayCommand]
    private void Remove(FavoriteItemViewModel? item)
    {
        if (item is not null)
        {
            _favorites.Remove(item.Path);
        }
    }

    [RelayCommand]
    private void MoveUp(FavoriteItemViewModel? item) => MoveBy(item, -1);

    [RelayCommand]
    private void MoveDown(FavoriteItemViewModel? item) => MoveBy(item, +1);

    private void MoveBy(FavoriteItemViewModel? item, int delta)
    {
        if (item is null)
        {
            return;
        }

        var index = Items.ToList().FindIndex(i => ReferenceEquals(i, item));
        if (index >= 0)
        {
            _favorites.Move(item.Path, index + delta);
        }
    }

    private void Rebuild() =>
        Items = _favorites.Items.Select(f => new FavoriteItemViewModel(f)).ToArray();
}

public sealed class FavoriteItemViewModel
{
    public FavoriteItemViewModel(FavoriteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Path = item.Path;
        IsDirectory = item.IsDirectory;
        Glyph = item.IsDirectory ? "📁" : "📄";

        var name = System.IO.Path.GetFileName(item.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        DisplayName = item.Label ?? (string.IsNullOrEmpty(name) ? item.Path : name);
    }

    public string Path { get; }

    public bool IsDirectory { get; }

    public string DisplayName { get; }

    public string Glyph { get; }
}
