using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Explorer.Core;
using Explorer.Shell.ContextMenu;
using Microsoft.Extensions.Logging;

namespace Explorer.Helper.ShellMenu;

/// <summary>
/// 네이티브 셸 컨텍스트 메뉴 호스트(별도 프로세스). 메인 앱이 화면 좌표와 대상 경로 목록을 넘기면
/// 그 메뉴를 띄우고 선택 동작을 실행한다. 서드파티 셸 확장이 힙을 손상시켜도 이 프로세스만 죽고 본체는 산다.
///
/// 두 가지 모드:
///  · 상주 모드(<c>--client &lt;파이프이름&gt;</c>): 앱이 만든 named pipe에 붙어 요청을 받을 때마다 메뉴를 띄운다.
///    매 우클릭마다 프로세스를 새로 띄우지 않아(첫 1회만) 셸 확장이 따뜻하게 유지돼 메뉴가 빠르게 뜬다.
///  · 1회성 모드(<c>&lt;x&gt; &lt;y&gt; &lt;요청파일&gt;</c>): 한 번 메뉴를 띄우고 종료(상주 헬퍼 실패 시 폴백).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var logger = CreateLogger();

        // 상주 모드: 앱의 파이프에 붙어 요청마다 메뉴를 띄운다.  인자: --client <파이프이름> [<부모PID>]
        if (args is ["--client", var pipeName, ..] && !string.IsNullOrWhiteSpace(pipeName))
        {
            int? parentPid = args.Length >= 3 && int.TryParse(args[2], out var pid) ? pid : null;
            return RunResidentClient(pipeName, parentPid, logger);
        }

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

    /// <summary>
    /// 상주 모드: 앱이 서버로 만든 named pipe에 클라이언트로 붙어, 요청(좌표+경로)을 받을 때마다 메뉴를 띄운다.
    /// 메뉴는 모달이라 한 번에 하나만 표시되며, 닫히면 다음 요청을 기다린다. 앱이 파이프를 닫으면(또는 종료하면)
    /// 읽기가 EOF가 되어 이 프로세스도 깔끔히 종료한다.
    /// </summary>
    private static int RunResidentClient(string pipeName, int? parentPid, FileLogger logger)
    {
        // 부모(메인 앱)가 강제 종료되면, 마침 메뉴를 모달로 띄우고 있어 파이프 EOF를 못 보는 상태라도
        // 이 헬퍼가 고아로 남지 않도록 부모 종료를 감시해 즉시 자신을 종료한다.
        if (parentPid is { } pid)
        {
            StartParentWatchdog(pid, logger);
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(10_000); // 앱이 막 spawn했으니 즉시 붙는다(최대 10초).

            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            // AutoFlush=false — 요청/응답 라운드트립이 막히지 않도록 모든 쓰기 뒤에는 반드시 Flush() 해야 한다.
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };

            logger.LogInformation("셸 메뉴 헬퍼(상주) 연결됨: {Pipe}", pipeName);

            while (true)
            {
                var request = ShellMenuProtocol.ReadRequest(reader);
                if (request is null)
                {
                    break; // 앱이 파이프를 닫음 — 종료.
                }

                var (screenX, screenY, rawPaths) = request.Value;
                var paths = rawPaths.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (paths.Length > 0)
                {
                    // 서드파티 확장이 던지는 어떤 예외도 여기서 격리(프로세스 힙 손상은 이 프로세스만 죽이고 본체는 산다).
                    try
                    {
                        NativeContextMenuHost.Show(paths, screenX, screenY, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "상주 헬퍼 메뉴 표시 실패: ({X},{Y}) {Count}개", screenX, screenY, paths.Length);
                    }
                }

                // 메뉴가 닫혔음을 앱에 알린다(헬퍼 생존 확인 겸 직렬화).
                try
                {
                    writer.WriteLine(ShellMenuProtocol.Ack);
                    writer.Flush();
                }
                catch (IOException)
                {
                    break; // 앱이 사라짐.
                }
            }

            logger.LogInformation("셸 메뉴 헬퍼(상주) 종료");
            return 0;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "상주 헬퍼 파이프 연결 실패: {Pipe}", pipeName);
            return 4;
        }
    }

    /// <summary>부모 프로세스가 종료되면 이 헬퍼도 즉시 종료한다(모달 메뉴 표시 중이어도 메뉴와 함께 사라진다).</summary>
    private static void StartParentWatchdog(int parentPid, FileLogger logger)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 부모가 이미 없거나 접근 불가 — 바로 종료한다.
            }

            logger.LogInformation("부모 프로세스(pid={Pid}) 종료 감지 — 상주 헬퍼 종료", parentPid);
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "shellmenu-parent-watchdog",
        };

        thread.Start();
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
