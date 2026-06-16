using System.IO;
using Explorer.Core;
using Explorer.Shell.ContextMenu;
using Microsoft.Extensions.Logging;

namespace Explorer.Helper.ShellMenu;

/// <summary>
/// 네이티브 셸 컨텍스트 메뉴 호스트(별도 프로세스). 메인 앱이 화면 좌표와 대상 경로 목록을 넘기면
/// 그 메뉴를 띄우고 선택 동작을 실행한다. 서드파티 셸 확장이 힙을 손상시켜도 이 프로세스만 죽고 본체는 산다.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var logger = CreateLogger();

        // 인자: <screenX> <screenY> <요청파일>  (요청파일 = 대상 경로 목록, UTF-8 한 줄에 하나)
        if (args.Length < 3 || !int.TryParse(args[0], out var screenX) || !int.TryParse(args[1], out var screenY))
        {
            logger.LogWarning("잘못된 인자: {Args}", string.Join(" ", args));
            return 2;
        }

        var requestFile = args[2];
        string[] lines;
        try
        {
            lines = File.ReadAllLines(requestFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "요청 파일 읽기 실패: {File}", requestFile);
            return 3;
        }
        finally
        {
            TryDelete(requestFile);
        }

        var paths = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (paths.Length == 0)
        {
            return 0;
        }

        logger.LogInformation("셸 메뉴 표시: ({X},{Y}) {Count}개 항목", screenX, screenY, paths.Length);
        NativeContextMenuHost.Show(paths, screenX, screenY, logger);
        logger.LogInformation("셸 메뉴 종료");
        return 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 임시 파일 삭제 실패는 무시.
        }
    }

    private static FileLogger CreateLogger()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            return new FileLogger(Path.Combine(AppPaths.LogsDir, "shellmenu.log"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileLogger(null);
        }
    }
}
