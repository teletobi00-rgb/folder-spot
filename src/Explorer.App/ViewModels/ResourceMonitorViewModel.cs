using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.Core.Settings;

namespace Explorer.App.ViewModels;

/// <summary>
/// 이 프로세스의 CPU·메모리 사용량을 주기적으로(2초) 상태바에 노출한다(옵트인). DispatcherTimer라 UI 스레드에서
/// 돌고, 측정은 매니지드 API(Process.TotalProcessorTime 델타 + WorkingSet64)라 추가 의존성·권한이 없다.
/// 트레이로 숨겨 유휴가 되면 폴링을 멈춘다.
/// </summary>
public sealed partial class ResourceMonitorViewModel : ObservableObject, IResourceMonitor, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly DispatcherTimer _timer;
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _summary = string.Empty;

    public ResourceMonitorViewModel(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Update();
        SyncFromSettings();
    }

    /// <summary>설정의 표시 여부에 맞춰 폴링을 시작/중지한다(설정 저장 후 호출).</summary>
    public void SyncFromSettings()
    {
        IsEnabled = _settings.Current.ShowResourceMonitor;
        if (IsEnabled)
        {
            ResetBaseline();
            Update();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>창이 트레이로 숨겨지는 등 유휴일 때 폴링을 멈춘다.</summary>
    public void Pause() => _timer.Stop();

    /// <summary>다시 표시될 때 폴링을 재개한다(켜져 있을 때만).</summary>
    public void Resume()
    {
        if (IsEnabled)
        {
            ResetBaseline();
            _timer.Start();
        }
    }

    private void ResetBaseline()
    {
        try
        {
            _process.Refresh();
            _lastCpu = _process.TotalProcessorTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }

        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    private void Update()
    {
        try
        {
            _process.Refresh();
            var now = Stopwatch.GetTimestamp();
            var nowCpu = _process.TotalProcessorTime;
            var wallMs = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
            var cpuMs = (nowCpu - _lastCpu).TotalMilliseconds;
            _lastTimestamp = now;
            _lastCpu = nowCpu;

            var cpuPercent = wallMs > 0
                ? Math.Clamp(cpuMs / (wallMs * Environment.ProcessorCount) * 100.0, 0, 100)
                : 0;
            var memMb = _process.WorkingSet64 / (1024.0 * 1024.0);
            Summary = $"CPU {cpuPercent:0}% · 메모리 {memMb:N0}MB";
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 프로세스 정보 접근 실패 — 표시를 갱신하지 않는다.
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _process.Dispose();
    }
}
