using System.Windows;
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
        Action<bool> setFastIndexing,
        UpdateService updateService)
    {
        ArgumentNullException.ThrowIfNull(showMainWindow);
        ArgumentNullException.ThrowIfNull(toggleSearch);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(getFastIndexing);
        ArgumentNullException.ThrowIfNull(setFastIndexing);
        ArgumentNullException.ThrowIfNull(updateService);

        var menu = new ContextMenu();

        // 업데이트가 준비되면 보이는 항목(평소엔 숨김). 클릭하면 적용 후 재시작.
        var updateItem = new MenuItem { Header = "⬆ 업데이트 설치 후 재시작", Visibility = Visibility.Collapsed };
        updateItem.Click += (_, _) => updateService.ApplyAndRestart();
        menu.Items.Add(updateItem);
        WireUpdateReady(updateService, updateItem);

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
            Icon = LoadAppIcon(),
            ContextMenu = menu,
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => showMainWindow();
        _trayIcon.TrayMouseDoubleClick += (_, _) => showMainWindow();
    }

    /// <summary>업데이트 준비 이벤트(백그라운드 스레드) → UI 스레드에서 메뉴 항목 표시 + 알림.</summary>
    private void WireUpdateReady(UpdateService updateService, MenuItem updateItem)
    {
        updateService.UpdateReady += (_, _) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                updateItem.Header = $"⬆ 업데이트 설치 후 재시작 (v{updateService.PendingVersion})";
                updateItem.Visibility = Visibility.Visible;
                _trayIcon?.ShowNotification(
                    "업데이트 준비됨",
                    $"새 버전 v{updateService.PendingVersion}을(를) 받았습니다. 트레이 메뉴에서 설치하세요.");
            });
    }

    /// <summary>번들된 app.ico에서 트레이 아이콘을 로드한다(실패 시 시스템 기본).</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new System.Uri("pack://application:,,,/Assets/app.ico"));
            if (resource?.Stream is { } stream)
            {
                using (stream)
                {
                    return new System.Drawing.Icon(stream, new System.Drawing.Size(16, 16));
                }
            }
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.ArgumentException)
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
