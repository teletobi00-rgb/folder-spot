using System.Text.Json;
using System.Text.Json.Serialization;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace Explorer.Core.Favorites;

public sealed class JsonFavoritesService : IFavoritesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<JsonFavoritesService> _logger;
    private readonly Lock _gate = new();
    private List<FavoriteItem> _items = [];

    public JsonFavoritesService(string filePath, ILogger<JsonFavoritesService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<FavoriteItem> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            _items = ReadFromDisk();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(string path, bool? isDirectory = null)
    {
        var normalized = PathUtils.Normalize(path);

        lock (_gate)
        {
            if (IndexOf(normalized) >= 0)
            {
                return;
            }

            _items.Add(new FavoriteItem
            {
                Path = normalized,
                IsDirectory = isDirectory ?? Directory.Exists(normalized),
            });
            WriteToDisk();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string path)
    {
        var normalized = PathUtils.Normalize(path);

        lock (_gate)
        {
            var index = IndexOf(normalized);
            if (index < 0)
            {
                return;
            }

            _items.RemoveAt(index);
            WriteToDisk();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Move(string path, int newIndex)
    {
        var normalized = PathUtils.Normalize(path);

        lock (_gate)
        {
            var index = IndexOf(normalized);
            if (index < 0)
            {
                return;
            }

            var clamped = Math.Clamp(newIndex, 0, _items.Count - 1);
            if (clamped == index)
            {
                return;
            }

            var item = _items[index];
            _items.RemoveAt(index);
            _items.Insert(clamped, item);
            WriteToDisk();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOf(string normalizedPath) =>
        _items.FindIndex(i => string.Equals(i.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

    private List<FavoriteItem> ReadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<FavoritesDocument>(json, JsonOptions);
            return document?.Items?.Where(i => !string.IsNullOrWhiteSpace(i.Path)).ToList() ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "즐겨찾기 파일이 손상되어 빈 목록으로 대체합니다: {FilePath}", _filePath);
            BackupCorruptFile();
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "즐겨찾기 파일을 읽지 못했습니다: {FilePath}", _filePath);
            return [];
        }
    }

    private void WriteToDisk()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(directory ?? Path.GetTempPath(), Path.GetRandomFileName());
            var document = new FavoritesDocument { Items = [.. _items] };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패해도 메모리 목록은 유지 — 다음 변경에서 재시도된다.
            _logger.LogError(ex, "즐겨찾기 저장 실패: {FilePath}", _filePath);
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "손상된 즐겨찾기 파일 백업 실패: {FilePath}", _filePath);
        }
    }

    private sealed record FavoritesDocument
    {
        public int Version { get; init; } = 1;

        public List<FavoriteItem>? Items { get; init; }
    }
}
