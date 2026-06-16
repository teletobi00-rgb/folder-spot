using System.IO;
using Microsoft.Extensions.Logging;

namespace Explorer.Helper.ShellMenu;

/// <summary>헬퍼 진단용 최소 파일 로거(경로가 null이면 무동작). 외부 로깅 패키지 의존 없이 동작.</summary>
internal sealed class FileLogger(string? path) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => path is not null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (path is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 로깅 실패는 무시.
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
