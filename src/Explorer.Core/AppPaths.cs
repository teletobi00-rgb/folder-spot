namespace Explorer.Core;

/// <summary>앱 데이터 파일 경로의 단일 진실 공급원.</summary>
public static class AppPaths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Explorer");

    public static string SettingsFile { get; } = Path.Combine(AppDataDir, "settings.json");

    public static string FavoritesFile { get; } = Path.Combine(AppDataDir, "favorites.json");

    public static string KeymapFile { get; } = Path.Combine(AppDataDir, "keymap.json");

    public static string IndexDbFile { get; } = Path.Combine(AppDataDir, "index.db");

    public static string SearchUsageFile { get; } = Path.Combine(AppDataDir, "search-usage.json");

    public static string LogsDir { get; } = Path.Combine(AppDataDir, "logs");
}
