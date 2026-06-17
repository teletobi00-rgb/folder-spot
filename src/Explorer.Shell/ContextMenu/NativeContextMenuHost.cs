using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Explorer.Shell.ContextMenu;

/// <summary>
/// 항목들의 네이티브 셸 컨텍스트 메뉴를 실제로 호스팅·표시한다(QueryContextMenu/InvokeCommand).
/// 일부 서드파티 셸 확장이 이 과정에서 호출 프로세스의 힙을 손상(0xc0000374)시킬 수 있어,
/// 메인 앱이 아니라 별도 헬퍼 프로세스(Explorer.Helper.ShellMenu)에서 호출하는 것이 원칙이다.
/// </summary>
public static class NativeContextMenuHost
{
    /// <summary>경로들의 네이티브 컨텍스트 메뉴를 화면 좌표에 띄우고 선택을 실행한다. STA 스레드에서 호출.</summary>
    public static void Show(IReadOnlyList<string> paths, int screenX, int screenY, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        if (paths.Count == 0)
        {
            return;
        }

        // 메뉴를 소유할 전경(前景) 창을 만든다. owner가 NULL이면 TrackPopupMenuEx가 "바깥 클릭"으로 닫히지 않아
        // 사용자가 반드시 메뉴 항목을 골라야 한다(B3). 1x1 비가시 창을 전경으로 두면 바깥을 클릭할 때 메뉴가 닫힌다.
        var owner = User32.CreateWindowEx(
            0, "STATIC", null, User32.WindowStyles.WS_POPUP,
            screenX, screenY, 1, 1, HWND.NULL, HMENU.NULL, HINSTANCE.NULL, IntPtr.Zero);
        try
        {
            if (!owner.IsInvalid)
            {
                User32.SetForegroundWindow(owner);
            }

            var items = new List<ShellItem>(paths.Count);
            try
            {
                foreach (var path in paths)
                {
                    items.Add(new ShellItem(PathUtils.Normalize(path)));
                }

                // 해제 순서가 결정적으로 중요하다: 메뉴 → keepAlive → ShellItem.
                // keepAlive는 메뉴의 네이티브 리소스를 떠받치므로 메뉴보다 먼저 해제하면 이중 해제로 힙 손상이 난다.
                var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);
                try
                {
                    menu.PopulateMenu();
                    // owner 창을 넘겨 바깥 클릭으로 닫히게 한다.
                    menu.ShowContextMenu(new POINT(screenX, screenY), default, null, owner);
                }
                finally
                {
                    menu.Dispose();
                    keepAlive.Dispose();
                }
            }
            catch (Exception ex)
            {
                // 서드파티 셸 확장이 어떤 예외든 던질 수 있다 — 헬퍼 프로세스 안에서 격리해 로깅한다.
                logger.LogWarning(ex, "네이티브 컨텍스트 메뉴 호스팅 실패: {Paths}", string.Join(", ", paths));
            }
            finally
            {
                foreach (var item in items)
                {
                    item.Dispose();
                }
            }
        }
        finally
        {
            if (!owner.IsInvalid)
            {
                // MSDN 권장 워크어라운드: 메뉴 종료 후 owner에 WM_NULL을 보내 다음 클릭에서 메뉴가 확실히 사라지게 한다.
                User32.PostMessage((HWND)owner, 0u, IntPtr.Zero, IntPtr.Zero);
            }

            owner.Dispose(); // DestroyWindow
        }
    }
}
