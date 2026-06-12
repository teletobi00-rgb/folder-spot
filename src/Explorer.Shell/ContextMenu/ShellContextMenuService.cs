using System.Diagnostics;
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
}

public sealed class ShellContextMenuService : IShellContextMenuService
{
    private readonly ILogger<ShellContextMenuService> _logger;

    public ShellContextMenuService(ILogger<ShellContextMenuService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void ShowMenu(IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (paths is not { Count: > 0 })
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var items = new List<ShellItem>(paths.Count);
        try
        {
            foreach (var path in paths)
            {
                items.Add(new ShellItem(PathUtils.Normalize(path)));
            }

            // 해제 순서가 결정적으로 중요하다: 메뉴 → keepAlive → ShellItem.
            // keepAlive는 메뉴의 네이티브 리소스를 떠받치므로 메뉴보다 먼저 해제하면
            // 이중 해제로 힙 손상(0xc0000374)이 발생한다 (Vanara 공식 테스트 패턴 준수).
            var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);
            try
            {
                var createdMs = stopwatch.ElapsedMilliseconds;

                // QueryContextMenu(셸 확장 열거)가 가장 느린 단계 — 표시와 분리해 계측한다.
                // ShowContextMenu는 같은 옵션으로 만든 메뉴를 캐시 재사용한다.
                menu.PopulateMenu();
                var populatedMs = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug(
                    "컨텍스트 메뉴 준비: 항목 {CreateMs}ms + 확장 열거 {PopulateMs}ms ({Count}개 항목)",
                    createdMs, populatedMs - createdMs, paths.Count);

                menu.ShowContextMenu(new POINT(screenX, screenY));
            }
            finally
            {
                menu.Dispose();
                keepAlive.Dispose();
            }
        }
        catch (Exception ex)
        {
            // 서드파티 셸 확장이 in-proc에서 어떤 예외든 던질 수 있다(R-SHELLCRASH) — 앱 크래시로 번지지 않게 격리.
            _logger.LogWarning(ex, "셸 컨텍스트 메뉴 표시 실패: {Paths}", string.Join(", ", paths));
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
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
