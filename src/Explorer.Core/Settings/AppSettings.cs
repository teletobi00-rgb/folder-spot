namespace Explorer.Core.Settings;

/// <summary>앱 전역 설정 스냅샷. 변경은 항상 with 식으로 새 인스턴스를 만든다.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppTheme Theme { get; init; } = AppTheme.System;

    public bool ShowHiddenFiles { get; init; }
}
