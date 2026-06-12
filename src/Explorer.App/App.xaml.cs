using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Explorer.App.Services;
using Explorer.App.ViewModels;
using Explorer.App.Views;
using Explorer.Core;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Settings;
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

        services.AddSingleton<IFileSystemEnumerator, FileSystemEnumerator>();
        services.AddSingleton<IDriveProvider, DriveInfoDriveProvider>();
        services.AddSingleton<IFileLauncher, ShellFileLauncher>();
        services.AddSingleton<IShellIconProvider, ShellIconProvider>();
        services.AddSingleton<IFileOperationService>(provider => new ShellFileOperationService(
            () => Current?.MainWindow is { } window ? new WindowInteropHelper(window).Handle : 0,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ShellFileOperationService>>()));
        services.AddSingleton<IFileClipboardService, WpfFileClipboardService>();
        services.AddSingleton<IShellContextMenuService, ShellContextMenuService>();
        // 와처는 소유 VM이 수명을 관리한다 — 페인마다 하나씩 갖도록 transient (Phase 3 듀얼 페인 대비).
        services.AddTransient<IFolderWatcher>(provider => new FileSystemFolderWatcher(
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileSystemFolderWatcher>>()));

        services.AddSingleton<FileListViewModel>();
        services.AddSingleton<DriveSidebarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Information("OnStartup 진입");
        RegisterGlobalExceptionHandlers();
        _host.Start();
        Log.Information("호스트 시작 완료");

        var settings = _host.Services.GetRequiredService<ISettingsService>();
        settings.Load();
        _host.Services.GetRequiredService<IThemeService>().Apply(settings.Current.Theme);
        Log.Information("설정 로드/테마 적용 완료 (테마: {Theme})", settings.Current.Theme);

        var window = _host.Services.GetRequiredService<MainWindow>();
        Log.Information("MainWindow 인스턴스 생성 완료");
        MainWindow = window;
        window.Show();
        Log.Information("Explorer 시작 완료");
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
