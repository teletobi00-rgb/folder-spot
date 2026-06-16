using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Explorer.Shell.ContextMenu;

public interface IShellContextMenuService
{
    /// <summary>
    /// 항목들의 Windows 네이티브 컨텍스트 메뉴를 화면 좌표에 띄우고 선택된 동작을 실행한다.
    /// 반드시 UI(STA) 스레드에서 호출해야 한다 (메뉴가 메시지 펌프를 사용).
    /// </summary>
    void ShowMenu(IReadOnlyList<string> paths, int screenX, int screenY);

    /// <summary>
    /// 셸 확장 핸들러 DLL을 미리 로드해 첫 우클릭 지연을 줄인다.
    /// UI 스레드에서 호출해야 하며, 실제 작업은 idle 타임에 청크로 분산된다.
    /// </summary>
    void BeginWarmUp();

    /// <summary>Windows 속성 다이얼로그를 연다 (IContextMenu 호스팅 없이 직접 API 호출 — 안전).</summary>
    void ShowProperties(string path);
}

public sealed class ShellContextMenuService : IShellContextMenuService
{
    private readonly ILogger<ShellContextMenuService> _logger;
    private readonly string? _helperPath;

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

        // 기본: 별도 헬퍼 프로세스에서 호스팅 — 서드파티 셸 확장이 QueryContextMenu에서 힙을 손상(0xc0000374)시켜도
        // 헬퍼만 죽고 본체는 산다(이 머신의 특정 확장이 비탐색기 프로세스에서 호스팅만으로 크래시하는 문제 회피).
        if (_helperPath is { } helper && File.Exists(helper))
        {
            LaunchHelper(helper, paths, screenX, screenY);
            return;
        }

        // 헬퍼 미배포 시에만 in-proc 폴백(격리 없음 — 기능 유지 우선).
        _logger.LogWarning("셸 메뉴 헬퍼를 찾을 수 없어 in-proc로 표시합니다(확장에 따라 불안정할 수 있음).");
        NativeContextMenuHost.Show(paths, screenX, screenY, _logger);
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

    public void BeginWarmUp()
    {
        // 메뉴 객체를 미리 만드는 방식(보조 STA 스레드)은 일부 셸 확장에서 힙 손상을 일으켰다(0xc0000374).
        // 대신 비용의 본체인 "확장 DLL 로드"만 선수행한다: 레지스트리에서 컨텍스트 메뉴 핸들러
        // CLSID를 모아 UI 스레드 idle 타임에 청크 단위로 생성·즉시 해제. QueryContextMenu는 호출하지 않는다.
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var clsids = new Queue<Guid>(EnumerateContextMenuHandlerClsids());
        if (clsids.Count == 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var loaded = 0;
        var total = clsids.Count;

        void ProcessChunk()
        {
            var chunkStart = Stopwatch.GetTimestamp();
            while (clsids.Count > 0 && Stopwatch.GetElapsedTime(chunkStart).TotalMilliseconds < 30)
            {
                var clsid = clsids.Dequeue();
                try
                {
                    if (Type.GetTypeFromCLSID(clsid) is { } comType
                        && Activator.CreateInstance(comType) is { } handler)
                    {
                        Marshal.ReleaseComObject(handler);
                        loaded++;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // 확장 생성 실패는 흔하다(권한, 의존성) — 무시하고 다음으로.
                    _logger.LogTrace(ex, "핸들러 워밍업 실패: {Clsid}", clsid);
                }
            }

            if (clsids.Count > 0)
            {
                _ = dispatcher.BeginInvoke(ProcessChunk, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else
            {
                _logger.LogDebug(
                    "컨텍스트 메뉴 핸들러 워밍업 완료: {Loaded}/{Total}개, {ElapsedMs}ms",
                    loaded, total, stopwatch.ElapsedMilliseconds);
            }
        }

        _ = dispatcher.BeginInvoke(ProcessChunk, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>파일/폴더에 적용되는 컨텍스트 메뉴 핸들러 CLSID를 레지스트리에서 수집한다.</summary>
    private static HashSet<Guid> EnumerateContextMenuHandlerClsids()
    {
        string[] roots = [@"*\shellex\ContextMenuHandlers", @"AllFilesystemObjects\shellex\ContextMenuHandlers", @"Directory\shellex\ContextMenuHandlers", @"Folder\shellex\ContextMenuHandlers"];
        var clsids = new HashSet<Guid>();

        foreach (var root in roots)
        {
            try
            {
                using var rootKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(root);
                if (rootKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in rootKey.GetSubKeyNames())
                {
                    using var handlerKey = rootKey.OpenSubKey(subKeyName);
                    var raw = handlerKey?.GetValue(null) as string;
                    var candidate = string.IsNullOrWhiteSpace(raw) ? subKeyName : raw;
                    if (Guid.TryParse(candidate, out var clsid))
                    {
                        clsids.Add(clsid);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                // 일부 키 접근 불가 — 무시
            }
        }

        return clsids;
    }
}
