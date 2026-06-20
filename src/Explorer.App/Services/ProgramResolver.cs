using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Explorer.App.Services;

/// <summary>실행 명령("calc.exe" 같은 이름 또는 전체 경로)을 다룬다 — 아이콘용 실제 경로 해석 + 셸 실행.</summary>
public static class ProgramResolver
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";

    /// <summary>명령을 실제 실행 파일 경로로 해석. 못 찾으면 null(셸 실행 자체는 가능).</summary>
    public static string? ResolvePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim().Trim('"');

        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            return trimmed;
        }

        // App Paths 레지스트리 (calc.exe·msedge.exe 등 등록된 앱의 전체 경로)
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(AppPathsKey + trimmed);
            if (key?.GetValue(null) is string registered)
            {
                var path = registered.Trim().Trim('"');
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        // System32 (cmd.exe 등)
        var system32 = Path.Combine(Environment.SystemDirectory, trimmed);
        if (File.Exists(system32))
        {
            return system32;
        }

        // PATH 검색
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), trimmed);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // PATH 안의 잘못된 문자가 든 항목은 건너뛴다.
                }
            }
        }

        return null;
    }

    /// <summary>표시용 이름(확장자 없는 파일명).</summary>
    public static string DisplayNameFor(string command)
    {
        var resolved = ResolvePath(command) ?? command.Trim().Trim('"');
        var name = Path.GetFileNameWithoutExtension(resolved);
        return string.IsNullOrEmpty(name) ? command : name;
    }

    /// <summary>
    /// 명령을 셸로 실행. <paramref name="workingDirectory"/>가 유효한 폴더면 작업 폴더로 지정한다
    /// (cmd/PowerShell 등이 현재 위치에서 열리도록). 실패해도 조용히 무시(런처 클릭은 치명적 작업이 아니다).
    /// </summary>
    public static bool Launch(string command, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = command.Trim().Trim('"'),
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
            }

            Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
