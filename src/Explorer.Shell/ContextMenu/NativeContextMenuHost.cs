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
}
