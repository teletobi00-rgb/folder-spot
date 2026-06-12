using System.Diagnostics;
using System.IO;
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
    /// 셸 확장 DLL을 백그라운드에서 미리 로드해 첫 우클릭 지연을 줄인다. 시작 후 1회 호출.
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

            using var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);
            using (keepAlive)
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
        // STA COM은 메시지 펌프가 필요하다(일부 셸 확장이 cross-apartment 마샬링) —
        // Dispatcher.Run으로 펌프를 돌리고 워밍업 후 스스로 종료한다.
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(WarmUpCore);
            System.Windows.Threading.Dispatcher.Run();
        })
        {
            Name = "Explorer.MenuWarmUp",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void WarmUpCore()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // 파일용 + 폴더용 핸들러를 각각 한 번씩 열거시켜 확장 DLL을 프로세스에 적재한다.
            WarmUpFor(Environment.ProcessPath ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            WarmUpFor(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            _logger.LogDebug("컨텍스트 메뉴 워밍업 완료: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "컨텍스트 메뉴 워밍업 실패 — 무시하고 계속");
        }
        finally
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void WarmUpFor(string path)
    {
        using var item = new ShellItem(path);
        using var menu = ShellContextMenu.CreateFromItems([item], out var keepAlive);
        using (keepAlive)
        {
            menu.PopulateMenu();
        }
    }
}
