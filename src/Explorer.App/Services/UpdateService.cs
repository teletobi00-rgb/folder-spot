using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Explorer.App.Services;

/// <summary>
/// GitHub 릴리스 기반 자동 업데이트(Velopack). 설치본일 때만 동작하고, 개발/포터블 실행에선 no-op.
/// 백그라운드에서 확인·다운로드까지만 하고, 적용(재시작)은 사용자가 트레이에서 선택한다.
/// </summary>
public sealed class UpdateService
{
    // 공개 리포 — 인증 토큰 없이 릴리스를 읽는다.
    private const string RepositoryUrl = "https://github.com/teletobi00-rgb/folder-spot";

    private readonly ILogger<UpdateService> _logger;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService(ILogger<UpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>업데이트가 다운로드되어 적용 대기 중일 때 발생(트레이 알림용). 백그라운드 스레드에서 올라온다.</summary>
    public event EventHandler? UpdateReady;

    public bool IsUpdatePending => _pending is not null;

    public string? PendingVersion => _pending?.TargetFullRelease?.Version?.ToString();

    /// <summary>업데이트 확인 후 있으면 다운로드까지. 설치본이 아니거나 실패하면 조용히 끝난다.</summary>
    public async Task CheckAndDownloadAsync()
    {
        try
        {
            _manager ??= new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

            // 개발/포터블 실행(미설치)에선 업데이트 개념이 없다.
            if (!_manager.IsInstalled)
            {
                _logger.LogDebug("미설치 실행 — 업데이트 확인 생략");
                return;
            }

            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                _logger.LogDebug("최신 버전입니다");
                return;
            }

            _logger.LogInformation("업데이트 발견: {Version} — 다운로드 시작", info.TargetFullRelease.Version);
            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info;
            _logger.LogInformation("업데이트 다운로드 완료: {Version}", info.TargetFullRelease.Version);
            UpdateReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or IOException or InvalidOperationException
                or TimeoutException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "업데이트 확인/다운로드 실패");
        }
    }

    /// <summary>다운로드된 업데이트를 적용하고 앱을 재시작한다(트레이에서 호출).</summary>
    public void ApplyAndRestart()
    {
        if (_manager is { } manager && _pending is { } pending)
        {
            _logger.LogInformation("업데이트 적용 후 재시작: {Version}", pending.TargetFullRelease.Version);
            manager.ApplyUpdatesAndRestart(pending);
        }
    }
}
