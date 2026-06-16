using System.Reflection;

namespace Explorer.App;

/// <summary>앱 메타데이터(버전 등). 게시 시 publish.ps1의 -p:Version으로 어셈블리 버전이 찍힌다.</summary>
public static class AppInfo
{
    /// <summary>표시용 버전 문자열(예: "v1.0.1"). 버전을 못 읽으면 빈 문자열.</summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
    }
}
