using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace Explorer.Shell.ContextMenu;

public interface IShellContextMenuService
{
    /// <summary>
    /// 항목들의 Windows 네이티브 컨텍스트 메뉴를 화면 좌표에 띄우고 선택된 동작을 실행한다.
    /// 반드시 UI(STA) 스레드에서 호출해야 한다 (메뉴가 메시지 펌프를 사용).
    /// </summary>
    void ShowMenu(IReadOnlyList<string> paths, int screenX, int screenY);

    /// <summary>
    /// 셸 메뉴 헬퍼를 미리 띄워 첫 우클릭 지연을 없앤다.
    /// UI 스레드에서 호출해도 되며, 실제 작업은 백그라운드에서 이뤄진다.
    /// </summary>
    void BeginWarmUp();

    /// <summary>Windows 속성 다이얼로그를 연다 (IContextMenu 호스팅 없이 직접 API 호출 — 안전).</summary>
    void ShowProperties(string path);
}

/// <summary>
/// 네이티브 셸 컨텍스트 메뉴를 <b>상주 헬퍼 프로세스</b>에서 호스팅한다.
///
/// 매 우클릭마다 헬퍼를 새로 띄우면 프로세스 기동(CLR·어셈블리 로드)과 COM·셸 확장 콜드 초기화가 반복돼
/// 메뉴가 느리게 떴다. 그래서 헬퍼를 한 번만 띄워 named pipe로 붙여 두고, 우클릭마다 좌표·경로만 보낸다.
/// 셸 확장이 헬퍼에 따뜻하게 유지돼 두 번째 우클릭부터는 거의 즉시 뜬다.
///
/// 안전:
///  · 서드파티 셸 확장이 힙을 손상(0xc0000374)시키면 <b>헬퍼만</b> 죽고 본체는 산다(별도 프로세스 격리 유지).
///  · 헬퍼가 죽으면(파이프 끊김) 자동으로 다시 띄우고, 두 번 실패하면 1회성 헬퍼로 폴백한다.
///  · 누적 부작용을 막기 위해 일정 횟수(<see cref="RecycleAfterMenus"/>)마다 헬퍼를 새로 띄운다.
/// </summary>
public sealed class ShellContextMenuService : IShellContextMenuService, IDisposable
{
    /// <summary>이만큼 메뉴를 띄우면 헬퍼를 새로 spawn한다(셸 확장 누적 부작용 방지).</summary>
    private const int RecycleAfterMenus = 50;

    /// <summary>헬퍼가 파이프에 붙기를 기다리는 시간(첫 우클릭/사전 기동 시 1회). 정상이면 수십 ms 내 연결되며, 헬퍼가 깨졌을 때 폴백까지의 대기 상한이기도 하다.</summary>
    private const int ConnectTimeoutMs = 5_000;

    private readonly ILogger<ShellContextMenuService> _logger;
    private readonly string? _helperPath;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1); // 메뉴는 한 번에 하나 — 표시 중이면 새 우클릭은 무시한다.

    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _helper;
    private int _menusSinceSpawn;
    private bool _disposed;

    public ShellContextMenuService(ILogger<ShellContextMenuService> logger, string? helperPath = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _helperPath = helperPath;
    }

    public void ShowMenu(IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (paths is not { Count: > 0 })
        {
            return;
        }

        // 상주 헬퍼 경로가 있으면 그쪽으로 — 첫 호출에 한 번만 spawn하고 이후엔 재사용한다.
        if (_helperPath is { } helper && File.Exists(helper))
        {
            // 이미 메뉴를 표시 중이면(직전 메뉴가 아직 안 닫힘) 무시 — 메뉴 중복/스레드 누적 방지.
            if (!_gate.Wait(0))
            {
                return;
            }

            var snapshot = paths.ToArray();

            // UI(STA) 스레드를 막지 않도록 백그라운드에서 전송한다(메뉴는 헬퍼 프로세스에서 모달로 뜬다).
            _ = Task.Run(() =>
            {
                try
                {
                    SendToResidentHelper(helper, snapshot, screenX, screenY);
                }
                finally
                {
                    _gate.Release();
                }
            });
            return;
        }

        // 헬퍼 미배포 시에만 in-proc 폴백(격리 없음 — 기능 유지 우선).
        _logger.LogWarning("셸 메뉴 헬퍼를 찾을 수 없어 in-proc로 표시합니다.");
        NativeContextMenuHost.Show(paths, screenX, screenY, _logger);
    }

    public void BeginWarmUp()
    {
        if (_helperPath is not { } helper || !File.Exists(helper))
        {
            return;
        }

        // 상주 헬퍼를 시작 시 백그라운드에서 미리 띄워 연결해 둔다 — 첫 우클릭도 즉시 뜨도록
        // (프로세스 spawn·CLR 기동·어셈블리 로드 비용을 우클릭 전에 미리 치른다).
        // UI 스레드를 막지 않으며, 실패해도 무방하다(첫 ShowMenu에서 다시 시도한다).
        // 끄려면 EXPLORER_DISABLE_MENU_WARMUP=1 (App에서 가드) — 그 경우 첫 우클릭 때 지연 spawn한다.
        _ = Task.Run(() =>
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    EnsureHelper(helper);
                }
                catch (Exception ex) when (IsHelperFailure(ex))
                {
                    _logger.LogDebug(ex, "상주 셸 메뉴 헬퍼 사전 기동 실패 — 첫 우클릭 시 재시도");
                }
            }
        });
    }

    /// <summary>좌표·경로를 상주 헬퍼에 보내 메뉴를 띄운다(백그라운드 스레드에서 호출). 실패 시 재기동·폴백.</summary>
    private void SendToResidentHelper(string helper, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Process helperProcess;
            StreamReader reader;

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    EnsureHelper(helper);
                }
                catch (Exception ex) when (IsHelperFailure(ex))
                {
                    _logger.LogWarning(ex, "상주 셸 메뉴 헬퍼 기동 실패 (시도 {Attempt})", attempt + 1);
                    TeardownHelper();
                    continue; // 새로 spawn해서 재시도.
                }

                helperProcess = _helper!;
                reader = _reader!;

                try
                {
                    // 헬퍼가 메뉴 창을 전경으로 올릴 수 있도록 매 요청 직전에 포그라운드 권한을 부여한다.
                    // (앱이 전경이 아니면 실패할 수 있으나, 메뉴는 우클릭 직후 호출돼 보통 전경이다.)
                    if (!User32.AllowSetForegroundWindow((uint)helperProcess.Id))
                    {
                        _logger.LogDebug("AllowSetForegroundWindow 실패 — 메뉴가 다른 창 뒤로 갈 수 있음(앱이 전경이 아님)");
                    }

                    ShellMenuProtocol.WriteRequest(_writer!, screenX, screenY, paths);
                }
                catch (Exception ex) when (IsHelperFailure(ex))
                {
                    _logger.LogWarning(ex, "상주 셸 메뉴 헬퍼 전송 실패 (시도 {Attempt})", attempt + 1);
                    TeardownHelper();
                    continue; // 메뉴는 아직 안 떴으니 재시도 안전.
                }
            }

            // 메뉴가 닫힐 때까지(헬퍼가 ack를 보낼 때까지) 락 밖에서 대기 — 종료/워밍업이 막히지 않도록.
            string? ack = null;
            try
            {
                ack = reader.ReadLine();
            }
            catch (Exception ex) when (IsHelperFailure(ex))
            {
                _logger.LogDebug(ex, "상주 셸 메뉴 헬퍼 ack 대기 실패");
            }

            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(_helper, helperProcess))
                {
                    return; // 그 사이 재활용/해제됨.
                }

                if (ack is null)
                {
                    // 헬퍼가 메뉴 도중/직후 사라짐 — 정리만(메뉴 중복 방지를 위해 재시도하지 않는다).
                    TeardownHelper();
                }
                else if (++_menusSinceSpawn >= RecycleAfterMenus)
                {
                    // 셸 확장 누적 부작용을 막기 위해 일정 횟수마다 헬퍼를 새로 띄운다.
                    TeardownHelper();
                }
            }

            return; // 요청을 보냈으면 종료(성공/단발 실패 무관).
        }

        // 두 번 모두 기동/전송 실패 → 1회성 헬퍼로 폴백.
        // (참고: 1회성 폴백은 fire-and-forget이라 _gate가 메뉴 닫힘까지 유지되지 않는다. 이미 이중 실패한
        //  드문 경로라 그 사이 메뉴 중복 가능성은 감수한다 — 변경 전(매 클릭 독립) 동작과 동일.)
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        _logger.LogWarning("상주 헬퍼 사용 불가 — 1회성 헬퍼로 폴백합니다.");
        LaunchHelper(helper, paths, screenX, screenY);
    }

    /// <summary>상주 헬퍼가 살아 있고 파이프가 붙어 있으면 그대로, 아니면 새로 띄워 연결한다. 실패 시 예외.</summary>
    private void EnsureHelper(string helper)
    {
        if (_pipe is { IsConnected: true } && _helper is { HasExited: false } && _reader is not null && _writer is not null)
        {
            return;
        }

        TeardownHelper();

        // 인스턴스마다 고유한 파이프 이름 — 다른 프로세스가 붙지 못하게.
        var pipeName = "FolderSpot.ShellMenu." + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = helper,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--client");
            startInfo.ArgumentList.Add(pipeName);
            // 앱 PID — 헬퍼가 부모 종료를 감시해 강제 종료 시에도 고아로 남지 않게 한다.
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("셸 메뉴 헬퍼 프로세스를 시작하지 못했습니다.");

            using (var cts = new CancellationTokenSource(ConnectTimeoutMs))
            {
                try
                {
                    pipe.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                        // 이미 종료 — 무시.
                    }

                    process.Dispose();
                    throw;
                }
            }

            _pipe = pipe;
            _helper = process;
            _reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            // AutoFlush=false — WriteRequest가 항상 마지막에 Flush하므로 안전(쓰기 뒤 Flush 필수).
            _writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
            _menusSinceSpawn = 0;
            _logger.LogInformation("상주 셸 메뉴 헬퍼 시작: pid={Pid}", process.Id);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// <summary>파이프·스트림을 닫고(헬퍼는 EOF로 스스로 종료) 헬퍼 프로세스를 정리한다.</summary>
    private void TeardownHelper()
    {
        // 파이프를 먼저 닫으면 헬퍼의 ReadRequest가 EOF가 되어 스스로 종료한다.
        SafeDispose(_writer);
        SafeDispose(_reader);
        SafeDispose(_pipe);
        _writer = null;
        _reader = null;
        _pipe = null;

        if (_helper is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // 이미 종료 — 무시.
            }

            SafeDispose(process);
            _helper = null;
        }

        _menusSinceSpawn = 0;
    }

    private void LaunchHelper(string helper, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        try
        {
            // 경로 목록은 인자 길이/특수문자 한계를 피해 임시 파일로 전달(헬퍼가 읽고 삭제).
            var requestFile = Path.Combine(
                Path.GetTempPath(), $"folderspot-menu-{Guid.NewGuid():N}.txt");
            File.WriteAllLines(requestFile, paths);

            var startInfo = new ProcessStartInfo
            {
                FileName = helper,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(screenX.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(screenY.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(requestFile);

            var process = Process.Start(startInfo);
            if (process is not null)
            {
                // 헬퍼 창이 메뉴 포커스를 가질 수 있도록 포그라운드 전환을 허용한다.
                User32.AllowSetForegroundWindow((uint)process.Id);
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "셸 메뉴 헬퍼 실행 실패: {Paths}", string.Join(", ", paths));
        }
    }

    public void ShowProperties(string path)
    {
        try
        {
            Shell32.SHObjectProperties(HWND.NULL, Shell32.SHOP.SHOP_FILEPATH, PathUtils.Normalize(path), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "속성 다이얼로그 표시 실패: {Path}", path);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TeardownHelper();
        }

        // _gate(SemaphoreSlim)는 일부러 해제하지 않는다 — 비행 중인 Task.Run 람다가 종료 후에도
        // finally에서 _gate.Release()를 호출할 수 있어, 해제하면 ObjectDisposedException이 날 수 있다.
        // (SemaphoreSlim은 AvailableWaitHandle을 건드리지 않는 한 네이티브 핸들을 만들지 않아 누수도 사실상 없다.)
    }

    /// <summary>헬퍼/파이프 사용 중 정상적으로 날 수 있는(= 재기동으로 회복 가능한) 예외인지.</summary>
    private static bool IsHelperFailure(Exception ex) =>
        ex is IOException or InvalidOperationException or ObjectDisposedException
            or Win32Exception or TimeoutException or OperationCanceledException;

    private static void SafeDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception)
        {
            // 종료/정리 경로 — 무시.
        }
    }
}
