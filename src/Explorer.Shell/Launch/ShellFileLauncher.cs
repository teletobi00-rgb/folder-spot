using System.Diagnostics;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace Explorer.Shell.Launch;

/// <summary>ShellExecute(연결 프로그램)로 파일을 여는 기본 실행기.</summary>
public sealed class ShellFileLauncher : IFileLauncher
{
    private readonly ILogger<ShellFileLauncher> _logger;

    public ShellFileLauncher(ILogger<ShellFileLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void Launch(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        _logger.LogDebug("파일 실행: {Path}", fullPath);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
        });
    }
}
