using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Explorer.App.Services;

/// <summary>지정 폴더에서 터미널을 연다 — Windows Terminal → PowerShell → cmd 순으로 폴백.</summary>
public static class TerminalLauncher
{
    public static bool OpenAt(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        // Windows Terminal: -d 로 시작 디렉터리 지정.
        return TryStart("wt.exe", $"-d \"{directoryPath}\"", null)
            || TryStart("pwsh.exe", null, directoryPath)
            || TryStart("powershell.exe", null, directoryPath)
            || TryStart("cmd.exe", null, directoryPath);
    }

    private static bool TryStart(string fileName, string? arguments, string? workingDirectory)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
            };

            if (arguments is not null)
            {
                info.Arguments = arguments;
            }

            if (workingDirectory is not null)
            {
                info.WorkingDirectory = workingDirectory;
            }

            return Process.Start(info) is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // 해당 터미널이 없으면 다음 후보로.
            return false;
        }
    }
}
