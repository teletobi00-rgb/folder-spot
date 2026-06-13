using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using Explorer.Indexing.Index;
using Explorer.Indexing.Usn;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing.Sources;

/// <summary>USN 고속 인덱싱 결과.</summary>
public enum UsnStartResult
{
    /// <summary>MFT 열거 완료 — tailing이 백그라운드로 이어진다.</summary>
    Enumerated,

    /// <summary>실패(권한 거부/헬퍼 없음/볼륨 오류) — 호출자는 폴백해야 한다.</summary>
    Fallback,
}

/// <summary>
/// 메인 앱(비권한) 측 USN 소스. named pipe 서버를 띄우고 권한 헬퍼를 UAC로 실행해
/// MFT 배치 + USN 변경을 수신한다. 어떤 실패든 예외 없이 <see cref="UsnStartResult.Fallback"/>으로 보고한다.
/// </summary>
public sealed class UsnIndexSource : IDisposable
{
    private readonly string _helperPath;
    private readonly ILogger _logger;
    private readonly Func<string, string, bool> _launcher;
    private readonly CancellationTokenSource _shutdown = new();
    private NamedPipeServerStream? _server;
    private Process? _helper;
    private Task? _readLoop;

    /// <param name="launcher">파이프명/볼륨으로 헬퍼를 띄우고 성공 여부를 반환 (테스트 주입용, 기본은 UAC runas).</param>
    public UsnIndexSource(string helperPath, ILogger logger, Func<string, string, bool>? launcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        ArgumentNullException.ThrowIfNull(logger);
        _helperPath = helperPath;
        _logger = logger;
        _launcher = launcher ?? LaunchHelperProcess;
    }

    public string? RootPath { get; private set; }

    /// <summary>헬퍼 실행 파일이 존재하는지 — 소스 선택의 helperAvailable 판단에 쓴다.</summary>
    public static bool HelperExists(string helperPath) => File.Exists(helperPath);

    /// <summary>
    /// 볼륨을 열거하고 tailing을 시작한다. 열거 완료까지 대기한 결과를 반환하며,
    /// 성공이면 tailing은 백그라운드로 이어진다(Dispose까지).
    /// </summary>
    public async Task<UsnStartResult> StartAsync(
        string volumeRoot,
        Action<IReadOnlyList<IndexItem>> onBatch,
        Action<UsnChange> onChange,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        ArgumentNullException.ThrowIfNull(onBatch);
        ArgumentNullException.ThrowIfNull(onChange);
        RootPath = volumeRoot;

        var pipeName = "Explorer.Usn." + Guid.NewGuid().ToString("N");
        var enumComplete = new TaskCompletionSource<UsnStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _server = new NamedPipeServerStream(
                pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            if (!_launcher(pipeName, volumeRoot))
            {
                return UsnStartResult.Fallback;
            }

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            await _server.WaitForConnectionAsync(connectCts.Token).ConfigureAwait(false);

            _readLoop = Task.Run(() => ReadLoop(onBatch, onChange, enumComplete), CancellationToken.None);
            return await enumComplete.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or Win32Exception)
        {
            _logger.LogWarning(ex, "USN 소스 시작 실패 — 폴백: {Volume}", volumeRoot);
            return UsnStartResult.Fallback;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _server?.Dispose(); // 파이프 닫힘 → 헬퍼 종료
        _server = null;

        try
        {
            if (_helper is { HasExited: false })
            {
                _helper.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }

        _helper?.Dispose();
        _helper = null;
        _shutdown.Dispose();
    }

    private bool LaunchHelperProcess(string pipeName, string volumeRoot)
    {
        try
        {
            _helper = Process.Start(new ProcessStartInfo
            {
                FileName = _helperPath,
                Arguments = $"\"{pipeName}\" \"{volumeRoot}\"",
                UseShellExecute = true,
                Verb = "runas", // UAC 권한 상승
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
            return _helper is not null;
        }
        catch (Win32Exception ex)
        {
            // 1223 = ERROR_CANCELLED (사용자가 UAC 거부)
            _logger.LogInformation(ex, "권한 헬퍼 실행 취소/실패 — 폴백 사용 (코드 {Code})", ex.NativeErrorCode);
            return false;
        }
    }

    private void ReadLoop(
        Action<IReadOnlyList<IndexItem>> onBatch,
        Action<UsnChange> onChange,
        TaskCompletionSource<UsnStartResult> enumComplete)
    {
        try
        {
            using var reader = new BinaryReader(_server!, System.Text.Encoding.UTF8, leaveOpen: true);
            while (!_shutdown.IsCancellationRequested)
            {
                var message = UsnProtocol.Read(reader);
                if (message is null)
                {
                    break; // 파이프 끝
                }

                switch (message.Type)
                {
                    case UsnMessageType.Batch:
                        if (message.Batch.Count > 0)
                        {
                            onBatch(message.Batch);
                        }

                        break;
                    case UsnMessageType.EnumDone:
                        enumComplete.TrySetResult(UsnStartResult.Enumerated);
                        break;
                    case UsnMessageType.Change:
                        onChange(message.Change);
                        break;
                    case UsnMessageType.Error:
                        _logger.LogWarning("권한 헬퍼 오류: {Error}", message.Error);
                        enumComplete.TrySetResult(UsnStartResult.Fallback);
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException)
        {
            _logger.LogDebug(ex, "USN 읽기 루프 종료");
        }
        finally
        {
            // 열거 완료 신호 없이 끝났다면 폴백으로 마무리한다.
            enumComplete.TrySetResult(UsnStartResult.Fallback);
        }
    }
}
