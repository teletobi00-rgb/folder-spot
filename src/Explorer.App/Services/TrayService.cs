using System.Windows.Controls;
using H.NotifyIcon;

namespace Explorer.App.Services;

/// <summary>트레이 아이콘 — 창을 닫아도 핫키/인덱싱이 살아있고, 여기서만 진짜 종료한다.</summary>
public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _trayIcon;

    public void Initialize(
        Action showMainWindow,
        Action toggleSearch,
        IAutoStartService autoStart,
        AppLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(showMainWindow);
        ArgumentNullException.ThrowIfNull(toggleSearch);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(lifecycle);

        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "열기" };
        openItem.Click += (_, _) => showMainWindow();
        menu.Items.Add(openItem);

        var searchItem = new MenuItem { Header = "파일 검색", InputGestureText = "Alt+Space" };
        searchItem.Click += (_, _) => toggleSearch();
        menu.Items.Add(searchItem);

        menu.Items.Add(new Separator());

        var autoStartItem = new MenuItem
        {
            Header = "Windows 시작 시 실행",
            IsCheckable = true,
            IsChecked = autoStart.IsEnabled,
        };
        autoStartItem.Click += (_, _) => autoStart.SetEnabled(autoStartItem.IsChecked);
        menu.Items.Add(autoStartItem);

        // 외부에서 Run 키가 바뀌었을 수 있으니 메뉴를 열 때마다 실제 상태를 다시 읽는다.
        menu.Opened += (_, _) => autoStartItem.IsChecked = autoStart.IsEnabled;

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (_, _) => lifecycle.RequestExit();
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Explorer 파일 탐색기",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = menu,
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => showMainWindow();
        _trayIcon.TrayMouseDoubleClick += (_, _) => showMainWindow();
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
