using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Explorer.Core.Search;

/// <summary>검색 결과 사용 빈도(MRU 가중치) 저장소. 자주 여는 항목이 위로 온다.</summary>
public interface ISearchUsageStore
{
    void Load();

    int GetCount(string path);

    void Record(string path);
}

public sealed class JsonSearchUsageStore : ISearchUsageStore
{
    private const int MaxEntries = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonSearchUsageStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public JsonSearchUsageStore(string filePath, ILogger<JsonSearchUsageStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    public void Load()
    {
        lock (_gate)
        {
            _counts = ReadFromDisk();
        }
    }

    public int GetCount(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        lock (_gate)
        {
            return _counts.GetValueOrDefault(path);
        }
    }

    public void Record(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (_gate)
        {
            _counts[path] = _counts.GetValueOrDefault(path) + 1;

            // 용량 초과 시 사용 횟수가 가장 적은 항목부터 정리한다.
            if (_counts.Count > MaxEntries)
            {
                foreach (var stale in _counts.OrderBy(kv => kv.Value)
                    .Take(_counts.Count - MaxEntries).Select(kv => kv.Key).ToArray())
                {
                    _counts.Remove(stale);
                }
            }
        }

        // 디스크 쓰기는 UI 스레드를 막지 않도록 백그라운드에서 (락은 쓰기 직렬화용으로만 재획득).
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                WriteToDisk();
            }
        });
    }

    private Dictionary<string, int> ReadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(
                File.ReadAllText(_filePath), JsonOptions);
            return loaded is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "검색 사용 기록을 읽지 못해 초기화합니다: {Path}", _filePath);
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_counts, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "검색 사용 기록 저장 실패: {Path}", _filePath);
        }
    }
}
