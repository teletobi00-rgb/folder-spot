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
        AppLifecycle lifecycle,
        Func<bool> getFastIndexing,
        Action<bool> setFastIndexing)
    {
        ArgumentNullException.ThrowIfNull(showMainWindow);
        ArgumentNullException.ThrowIfNull(toggleSearch);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(getFastIndexing);
        ArgumentNullException.ThrowIfNull(setFastIndexing);

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

        var fastIndexItem = new MenuItem
        {
            Header = "빠른 인덱싱 (NTFS·관리자)",
            IsCheckable = true,
            IsChecked = getFastIndexing(),
            ToolTip = "켜면 다음 시작부터 MFT/USN 고속 인덱싱을 사용합니다(관리자 권한 1회 동의 필요).",
        };
        fastIndexItem.Click += (_, _) =>
        {
            setFastIndexing(fastIndexItem.IsChecked);
            _trayIcon?.ShowNotification(
                "빠른 인덱싱",
                fastIndexItem.IsChecked
                    ? "다음 앱 시작부터 적용됩니다 (관리자 권한 동의 필요)."
                    : "다음 앱 시작부터 일반 인덱싱으로 돌아갑니다.");
        };
        menu.Items.Add(fastIndexItem);

        // 외부에서 Run 키가 바뀌었을 수 있으니 메뉴를 열 때마다 실제 상태를 다시 읽는다.
        menu.Opened += (_, _) =>
        {
            autoStartItem.IsChecked = autoStart.IsEnabled;
            fastIndexItem.IsChecked = getFastIndexing();
        };

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (_, _) => lifecycle.RequestExit();
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Folder Spot",
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
