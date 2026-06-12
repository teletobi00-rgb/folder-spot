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

        var items = new List<ShellItem>(paths.Count);
        try
        {
            foreach (var path in paths)
            {
                items.Add(new ShellItem(PathUtils.Normalize(path)));
            }

            var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);
            using (keepAlive)
            {
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
}
