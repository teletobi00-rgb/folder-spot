using Microsoft.Win32;

namespace Explorer.App.Services;

public interface IAutoStartService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>HKCU Run 키 기반 자동 시작 (관리자 권한 불필요).</summary>
public sealed class RegistryAutoStartService : IAutoStartService
{
    private const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ExplorerFileManager";

    private readonly string _runKeyPath;

    public RegistryAutoStartService(string? runKeyPath = null)
    {
        _runKeyPath = runKeyPath ?? DefaultRunKey;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
