using System.IO;

namespace Explorer.Indexing.Sources;

/// <summary>
/// 인덱싱에서 제외할 정크/시스템 트리 — 사용자가 거의 검색하지 않으면서 노드 수(=메모리)만 키우는 경로들.
/// 실제 문서/코드는 그대로 인덱싱하되, 컴포넌트 스토어·패키지 캐시·VCS 내부·앱 캐시만 제외한다.
/// </summary>
public static class IndexExclusions
{
    /// <summary>어느 깊이든 디렉터리 '이름'이 일치하면 통째로 제외(하위 전체 스킵).</summary>
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        ".git",
        ".hg",
        ".svn",
        "__pycache__",
        "$Recycle.Bin",
        "System Volume Information",
        "WinSxS", // C:\Windows\WinSxS — 컴포넌트 스토어(수만 개, 검색 무의미)
        "$RECYCLE.BIN",
    };

    /// <summary>전체 경로에 이 조각이 들어가면 제외(특정 절대 하위 경로의 캐시/임시 트리).</summary>
    private static readonly string[] ExcludedFragments =
    [
        @"\Windows\Installer\",
        @"\Windows\servicing\",
        @"\Windows\SoftwareDistribution\",
        @"\Windows\Temp\",
        @"\Windows\Prefetch\",
        @"\Windows\Logs\",
        @"\ProgramData\Package Cache\",
        @"\AppData\Local\Temp\",
        @"\AppData\Local\Packages\",
        @"\AppData\Local\Microsoft\WindowsApps\",
        @"\AppData\Local\Microsoft\Windows\INetCache\",
        @"\AppData\Local\Microsoft\Edge\",
        @"\AppData\Local\Google\Chrome\",
        @"\AppData\Local\npm-cache\",
        @"\AppData\Local\pip\Cache\",
        @"\.nuget\packages\",
        @"\.gradle\",
        @"\.cargo\",
    ];

    /// <summary>스캔 중 이 디렉터리(경로·이름)를 제외할지 — 재귀 진입/포함 판단용.</summary>
    public static bool IsExcludedDirectory(string fullPath, string name)
    {
        if (ExcludedNames.Contains(name))
        {
            return true;
        }

        foreach (var fragment in ExcludedFragments)
        {
            if (fullPath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>증분(FSW/USN) 경로가 제외 트리 아래인지 — 어느 세그먼트라도 제외 이름이거나 조각 일치면 제외.</summary>
    public static bool IsExcludedPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        foreach (var fragment in ExcludedFragments)
        {
            if (fullPath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ExcludedNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
