using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Explorer.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly Lock _gate = new();

    public JsonSettingsService(string filePath, ILogger<JsonSettingsService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        lock (_gate)
        {
            Current = ReadFromDisk();
        }
    }

    public AppSettings Update(Func<AppSettings, AppSettings> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        lock (_gate)
        {
            var updated = transform(Current)
                ?? throw new InvalidOperationException("설정 변환 함수는 null을 반환할 수 없습니다.");

            try
            {
                WriteToDisk(updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "설정 저장 실패 — 메모리 설정은 변경하지 않습니다: {FilePath}", _filePath);
                throw;
            }

            Current = updated;
            return updated;
        }
    }

    private AppSettings ReadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                BackupCorruptFile();
                return new AppSettings();
            }

            return loaded;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "설정 파일이 손상되어 기본값으로 대체합니다: {FilePath}", _filePath);
            BackupCorruptFile();
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "설정 파일을 읽지 못해 기본값을 사용합니다: {FilePath}", _filePath);
            return new AppSettings();
        }
    }

    private void WriteToDisk(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 쓰기 도중 크래시로 파일이 깨지지 않도록 같은 볼륨의 임시 파일에 쓴 뒤 원자적으로 교체한다.
        var tempPath = Path.Combine(directory ?? Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "임시 설정 파일 삭제 실패: {TempPath}", tempPath);
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
            _logger.LogWarning(ex, "손상된 설정 파일 백업 실패: {FilePath}", _filePath);
        }
    }
}
