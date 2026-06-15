using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Explorer.App.Input;
using Explorer.App.Services;
using Explorer.App.Services.Operations;
using Explorer.App.Services.Preview;
using Explorer.App.ViewModels;
using Explorer.App.Views;
using Explorer.Core;
using Explorer.Core.FileOperations;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using Explorer.Core.Input;
using Explorer.Core.Operations;
using Explorer.Core.Search;
using Explorer.Core.Settings;
using Explorer.Core.Undo;
using Explorer.Indexing;
using Explorer.Indexing.Persistence;
using Explorer.Indexing.Sources;
using Explorer.Preview;
using Explorer.Preview.Renderers;
using Explorer.Shell.Clipboard;
using Explorer.Shell.ContextMenu;
using Explorer.Shell.Drives;
using Explorer.Shell.FileOperations;
using Explorer.Shell.Icons;
using Explorer.Shell.Launch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Explorer.App;

public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>메인 윈도우 HWND를 UI 스레드에서 1회 캡처해 둔다 — 파일 작업은 전용 STA 워커 스레드에서
    /// 도는데 거기서 Application.MainWindow(DispatcherObject)에 접근하면 크로스 스레드 예외가 난다.
    /// 핸들 자체는 스레드 무관(IntPtr)하므로 이것만 공유한다.</summary>
    private static IntPtr _mainWindowHandle;

    /// <summary>View 계층의 글루 코드용 서비스 접근점 (ViewModel은 생성자 주입을 쓴다).</summary>
    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        // 호스트 빌드 실패까지 잡을 수 있도록 2단계 초기화(부트스트랩 로거 → 호스트 로거)를 쓴다.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Explorer", LogEventLevel.Debug)
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDir, "app-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateBootstrapLogger();
        Log.Information("앱 생성자 진입");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((_, _, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Explorer", LogEventLevel.Debug)
                .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    Path.Combine(AppPaths.LogsDir, "app-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true))
            .ConfigureServices(ConfigureServices)
            .Build();
        Log.Information("호스트 빌드 완료");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService>(provider => new JsonSettingsService(
            AppPaths.SettingsFile,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonSettingsService>>()));
        services.AddSingleton<IThemeService, WpfUiThemeService>();
        services.AddSingleton<ExtensionColorMap>();

        services.AddSingleton<IFileSystemEnumerator, FileSystemEnumerator>();
        services.AddSingleton<IDriveProvider, DriveInfoDriveProvider>();
        services.AddSingleton<IFileLauncher, ShellFileLauncher>();
        services.AddSingleton<IShellIconProvider, ShellIconProvider>();
        services.AddSingleton<IFileOperationService>(provider => new ShellFileOperationService(
            () => System.Threading.Volatile.Read(ref _mainWindowHandle),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ShellFileOperationService>>()));
        services.AddSingleton<IFileClipboardService, WpfFileClipboardService>();
        services.AddSingleton<IShellContextMenuService, ShellContextMenuService>();
        services.AddSingleton<IFavoritesService>(provider => new JsonFavoritesService(
            AppPaths.FavoritesFile,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonFavoritesService>>()));
        services.AddSingleton(KeyMap.LoadWithOverrides(AppPaths.KeymapFile));

        // 파일 인덱싱 파이프라인: 스냅샷 즉시 복원 → 백그라운드 재구축(USN 고속/재귀 폴백) → 증분 감시
        services.AddSingleton<FileIndexCatalog>();
        services.AddSingleton(provider => new SqliteIndexSnapshot(
            AppPaths.IndexDbFile,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SqliteIndexSnapshot>>()));
        services.AddSingleton<RecursiveScanSource>();
        services.AddSingleton(provider =>
        {
            // 옵트인(설정) + 권한 헬퍼 경로를 환경 옵션 위에 합친다. 설정은 OnStartup에서 host.Start 전에 로드된다.
            var settings = provider.GetRequiredService<ISettingsService>();
            return IndexingOptions.FromEnvironment() with
            {
                FastIndexingEnabled = settings.Current.UseFastIndexing,
                HelperPath = ResolveHelperPath(),
            };
        });
        services.AddSingleton<IndexingService>();
        services.AddHostedService(provider => provider.GetRequiredService<IndexingService>());

        // 미리보기 렌더러 — 순서가 우선순위, 마지막 Info는 항상 처리하는 폴백.
        services.AddSingleton<IPreviewRenderer, ImagePreviewRenderer>();
        services.AddSingleton<IPreviewRenderer, TextPreviewRenderer>();
        services.AddSingleton<IPreviewRenderer, MediaPreviewRenderer>();
        services.AddSingleton<IPreviewRenderer, ArchivePreviewRenderer>();
        // Office/PDF 등 등록된 IPreviewHandler가 있는 형식 → 실제 OLE 미리보기(Info 폴백보다 우선).
        services.AddSingleton<IPreviewRenderer, ShellPreviewRenderer>();
        // InfoPreviewRenderer는 (1) 마지막 폴백이자 (2) ShellPreviewRenderer가 네트워크 경로에 위임하는 대상.
        // 같은 인스턴스를 양쪽에서 쓰도록 concrete로도 등록한다.
        services.AddSingleton<InfoPreviewRenderer>();
        services.AddSingleton<IPreviewRenderer>(sp => sp.GetRequiredService<InfoPreviewRenderer>());
        services.AddSingleton<IPreviewRendererRegistry, PreviewRendererRegistry>();
        services.AddSingleton<QuickPreviewWindow>();

        // 작업 큐 파이프라인: 큐(직렬 워커) → 실행기(충돌 해소 + 셸 호출 + Undo 기록)
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IConflictPrompt, DialogConflictPrompt>();
        services.AddSingleton<IQueuedOperationExecutor, QueuedOperationExecutor>();
        services.AddSingleton<IOperationQueue>(provider => new OperationQueue(
            provider.GetRequiredService<IQueuedOperationExecutor>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OperationQueue>>()));
        // 와처는 소유 VM이 수명을 관리한다 — 페인마다 하나씩 갖도록 transient (Phase 3 듀얼 페인 대비).
        services.AddTransient<IFolderWatcher>(provider => new FileSystemFolderWatcher(
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileSystemFolderWatcher>>()));

        // 페인마다 독립 파일 목록을 가지므로 transient — WorkspaceViewModel이 팩토리로 생성/소유한다.
        services.AddTransient<FileListViewModel>();
        services.AddSingleton<WorkspaceViewModel>(provider => new WorkspaceViewModel(
            provider.GetRequiredService<FileListViewModel>,
            provider.GetRequiredService<IUndoService>(),
            provider.GetRequiredService<IPreviewRendererRegistry>()));
        services.AddSingleton<DriveSidebarViewModel>();
        services.AddSingleton<FavoritesViewModel>();
        services.AddSingleton<OperationQueueViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        // 전역 검색: Alt+Space 핫키 → 팝업 → 인덱스 질의 (+MRU 가중)
        services.AddSingleton<ISearchUsageStore>(provider => new JsonSearchUsageStore(
            AppPaths.SearchUsageFile,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonSearchUsageStore>>()));
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<AppLifecycle>();
        services.AddSingleton<IAutoStartService, RegistryAutoStartService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<SearchPopupViewModel>();
        services.AddSingleton<SearchPopupWindow>();
    }

    /// <summary>권한 헬퍼 실행 파일 경로를 찾는다 (배포: 앱 옆 / 개발: 형제 프로젝트 bin). 없으면 null.</summary>
    private static string? ResolveHelperPath()
    {
        const string helperExe = "Explorer.Helper.Elevated.exe";
        var baseDir = AppContext.BaseDirectory;

        // 1) 배포 시 앱 실행 파일과 같은 폴더
        var sideBySide = Path.Combine(baseDir, helperExe);
        if (File.Exists(sideBySide))
        {
            return sideBySide;
        }

        // 2) 개발 빌드: ...\src\Explorer.App\bin\<cfg>\net10.0-windows\ → 형제 헬퍼 bin
        var configuration = new DirectoryInfo(baseDir).Parent?.Name ?? "Debug"; // net10.0-windows의 부모 = <cfg>
        var devPath = Path.Combine(
            baseDir, "..", "..", "..", "..",
            "Explorer.Helper.Elevated", "bin", configuration, "net10.0-windows", helperExe);
        return File.Exists(devPath) ? Path.GetFullPath(devPath) : null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Information("OnStartup 진입");
        RegisterGlobalExceptionHandlers();

        // 호스티드 인덱싱 서비스가 시작될 때 옵트인 설정을 읽을 수 있도록 설정을 먼저 로드한다.
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        settings.Load();

        _host.Start();
        Log.Information("호스트 시작 완료");

        _host.Services.GetRequiredService<IThemeService>().Apply(settings.Current.Theme);
        Log.Information("설정 로드/테마 적용 완료 (테마: {Theme})", settings.Current.Theme);

        var window = _host.Services.GetRequiredService<MainWindow>();
        Log.Information("MainWindow 인스턴스 생성 완료");
        MainWindow = window;
        // 파일 작업용 owner HWND를 Show() 이전에 확보·캡처한다 — Show가 메시지를 펌프해 재진입
        // (트레이/Activated 콜백)으로 파일 작업이 먼저 돌더라도 STA 워커가 유효한 핸들을 읽도록.
        System.Threading.Volatile.Write(ref _mainWindowHandle, new WindowInteropHelper(window).EnsureHandle());
        window.Show();

        // 셸 확장 DLL 선로딩 — 첫 우클릭 메뉴 지연을 줄인다. (진단용 비활성화: EXPLORER_DISABLE_MENU_WARMUP=1)
        if (Environment.GetEnvironmentVariable("EXPLORER_DISABLE_MENU_WARMUP") != "1")
        {
            _host.Services.GetRequiredService<IShellContextMenuService>().BeginWarmUp();
        }

        SetUpGlobalSearch(window);
        Log.Information("Explorer 시작 완료");
    }

    /// <summary>전역 검색 핫키 + 팝업 + 트레이 배선.</summary>
    private void SetUpGlobalSearch(MainWindow window)
    {
        _host.Services.GetRequiredService<ISearchUsageStore>().Load();

        var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var popup = _host.Services.GetRequiredService<SearchPopupWindow>();
        var popupViewModel = _host.Services.GetRequiredService<SearchPopupViewModel>();

        popupViewModel.OpenFolderRequested += (_, path) =>
        {
            window.ShowFromTray();
            _ = mainViewModel.Workspace.ActiveFileList.NavigateToAsync(path);
        };
        popupViewModel.RevealRequested += async (_, target) =>
        {
            try
            {
                window.ShowFromTray();
                await mainViewModel.Workspace.ActiveFileList.NavigateToAsync(target.Directory);
                mainViewModel.Workspace.ActiveFileList.SelectByPath(target.FullPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "검색 결과 표시 실패: {Path}", target.FullPath);
            }
        };

        var keyMap = _host.Services.GetRequiredService<KeyMap>();
        var gesture = keyMap.GestureFor(KeyActions.GlobalSearch);
        if (string.IsNullOrWhiteSpace(gesture))
        {
            Log.Information("전역 검색 핫키가 비어 있어 등록하지 않습니다 (사용자 해제)");
        }
        else if (!_host.Services.GetRequiredService<IGlobalHotkeyService>().TryRegister(gesture, popup.Toggle))
        {
            Log.Warning("전역 검색 핫키({Gesture}) 등록 실패 — keymap.json에서 Global.Search를 변경할 수 있습니다", gesture);
            mainViewModel.Workspace.ActiveFileList.StatusMessage =
                $"전역 검색 핫키({gesture}) 등록 실패 — 다른 앱이 사용 중일 수 있습니다.";
        }

        var settings = _host.Services.GetRequiredService<ISettingsService>();
        _host.Services.GetRequiredService<TrayService>().Initialize(
            window.ShowFromTray,
            popup.Toggle,
            _host.Services.GetRequiredService<IAutoStartService>(),
            _host.Services.GetRequiredService<AppLifecycle>(),
            getFastIndexing: () => settings.Current.UseFastIndexing,
            setFastIndexing: enabled => settings.Update(s => s with { UseFastIndexing = enabled }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Explorer 종료");
        try
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        finally
        {
            _host.Dispose();
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "처리되지 않은 UI 예외");
            MessageBox.Show(
                $"예상치 못한 오류가 발생했습니다.\n\n{args.Exception.Message}\n\n로그 위치: {AppPaths.LogsDir}",
                "Explorer 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "처리되지 않은 도메인 예외 (프로세스 종료: {IsTerminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "관찰되지 않은 Task 예외");
            args.SetObserved();
        };
    }
}
