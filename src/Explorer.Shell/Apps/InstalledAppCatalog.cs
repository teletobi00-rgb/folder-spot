using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Explorer.Shell.Threading;
using Microsoft.Extensions.Logging;
using Vanara.Windows.Shell;

namespace Explorer.Shell.Apps;

/// <summary>설치된 프로그램/앱 카탈로그 — 전역 검색에서 파일과 함께 노출하고 실행한다.</summary>
public interface IInstalledAppCatalog
{
    /// <summary>이름/키워드 부분 일치 검색(정확>접두>부분). 빈 질의는 빈 결과.</summary>
    Task<IReadOnlyList<AppHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>앱을 실행한다(shell:AppsFolder 항목은 explorer 폴백).</summary>
    void Launch(InstalledApp app);

    /// <summary>백그라운드에서 카탈로그를 미리 구성한다(첫 검색 지연 제거).</summary>
    Task WarmUpAsync();
}

/// <summary>
/// shell:AppsFolder(Win32+UWP, 현지화 이름·아이콘) 열거 + 콘솔/시스템 도구 큐레이션.
/// 한 번만 STA 워커에서 구성해 캐시하고, 이후 검색은 인메모리로 처리한다.
/// </summary>
public sealed class InstalledAppCatalog : IInstalledAppCatalog, IDisposable
{
    private static readonly char[] TokenSeparators = [' ', '.', '_', '-', '\\', '/', '!', ':', '(', ')', '[', ']'];

    private readonly ILogger<InstalledAppCatalog> _logger;
    private readonly StaWorker _worker = new("Explorer.AppCatalog");
    private readonly Lock _gate = new();
    private Task<IReadOnlyList<InstalledApp>>? _build;

    public InstalledAppCatalog(ILogger<InstalledAppCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task WarmUpAsync() => EnsureBuilt();

    public async Task<IReadOnlyList<AppHit>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
        {
            return [];
        }

        var apps = await EnsureBuilt().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = query.Trim().ToLowerInvariant();
        var hits = new List<AppHit>();
        foreach (var app in apps)
        {
            var rank = RankOf(app, normalized);
            if (rank >= 0)
            {
                hits.Add(new AppHit(app, rank));
            }
        }

        return [.. hits
            .OrderBy(h => h.Rank)
            .ThenBy(h => h.App.Name.Length)
            .ThenBy(h => h.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)];
    }

    public void Launch(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // AppsFolder(UWP/데스크톱 앱)는 explorer 경유 실행이 표준이자 가장 안정적이다.
        if (app.LaunchUri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryStart("explorer.exe", app.LaunchUri))
            {
                TryStart(app.LaunchUri, null);
            }

            return;
        }

        TryStart(app.LaunchUri, null);
    }

    public void Dispose() => _worker.Dispose();

    private Task<IReadOnlyList<InstalledApp>> EnsureBuilt()
    {
        lock (_gate)
        {
            return _build ??= BuildAsync();
        }
    }

    private async Task<IReadOnlyList<InstalledApp>> BuildAsync()
    {
        var apps = await _worker.RunAsync(BuildList).ConfigureAwait(false);
        _logger.LogInformation("설치된 앱 카탈로그 구성: {Count}개", apps.Count);
        return apps;
    }

    private IReadOnlyList<InstalledApp> BuildList()
    {
        // LaunchUri 기준 중복 제거 — AppsFolder가 우선(현지화 이름·아이콘), 큐레이션은 빠진 것만 보강.
        var byUri = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in EnumerateAppsFolder())
        {
            byUri[app.LaunchUri] = app;
        }

        foreach (var app in CuratedEssentials())
        {
            byUri.TryAdd(app.LaunchUri, app);
        }

        return [.. byUri.Values];
    }

    private List<InstalledApp> EnumerateAppsFolder()
    {
        var result = new List<InstalledApp>();
        try
        {
            using var folder = new ShellFolder("shell:AppsFolder");
            foreach (var item in folder)
            {
                try
                {
                    var name = item.GetDisplayName(ShellItemDisplayString.NormalDisplay);
                    var aumid = item.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(aumid))
                    {
                        var uri = "shell:AppsFolder\\" + aumid;
                        result.Add(new InstalledApp
                        {
                            Name = name,
                            LaunchUri = uri,
                            IconSource = uri,
                            Keywords = BuildKeywords(name, aumid),
                        });
                    }
                }
                catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
                {
                    _logger.LogDebug(ex, "AppsFolder 항목 읽기 실패");
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or FileNotFoundException)
        {
            // 열거 실패 시 큐레이션 목록만으로 동작(완전 실패 회피).
            _logger.LogWarning(ex, "AppsFolder 열거에 실패해 기본 도구 목록만 사용합니다.");
        }

        return result;
    }

    private static ImmutableArray<string> BuildKeywords(string name, string aumid)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTokens(set, name);
        AddTokens(set, aumid);

        // AUMID가 경로 꼴(데스크톱 앱)이면 실행 파일명도 키워드로 — 영문 명령 매칭("winword" 등).
        if (aumid.Contains('\\', StringComparison.Ordinal))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(aumid);
                if (!string.IsNullOrEmpty(fileName))
                {
                    set.Add(fileName.ToLowerInvariant());
                }
            }
            catch (ArgumentException)
            {
                // 잘못된 경로 문자는 무시.
            }
        }

        return [.. set];
    }

    private static void AddTokens(HashSet<string> set, string text)
    {
        set.Add(text.ToLowerInvariant());
        foreach (var token in text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 1)
            {
                set.Add(token.ToLowerInvariant());
            }
        }
    }

    private static int RankOf(InstalledApp app, string query)
    {
        var rank = MatchRank(app.Name.ToLowerInvariant(), query);
        foreach (var keyword in app.Keywords)
        {
            var candidate = MatchRank(keyword, query);
            if (candidate >= 0 && (rank < 0 || candidate < rank))
            {
                rank = candidate;
            }

            if (rank == 0)
            {
                break;
            }
        }

        return rank;
    }

    private static int MatchRank(string text, string query) =>
        text.Length == 0 ? -1
        : string.Equals(text, query, StringComparison.Ordinal) ? 0
        : text.StartsWith(query, StringComparison.Ordinal) ? 1
        : text.Contains(query, StringComparison.Ordinal) ? 2
        : -1;

    private bool TryStart(string fileName, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger.LogWarning(ex, "앱 실행 실패: {Target}", fileName);
            return false;
        }
    }

    /// <summary>AppsFolder가 현지화 이름으로만 노출해 영문 명령으로는 안 잡히는 콘솔/시스템 도구.</summary>
    private static IEnumerable<InstalledApp> CuratedEssentials()
    {
        (string Exe, string Display, string[] Keys)[] essentials =
        [
            ("cmd.exe", "명령 프롬프트 (cmd)", ["cmd", "command", "commandprompt"]),
            ("powershell.exe", "Windows PowerShell", ["powershell", "pwsh", "ps"]),
            ("calc.exe", "계산기 (Calculator)", ["calc", "calculator"]),
            ("notepad.exe", "메모장 (Notepad)", ["notepad"]),
            ("mspaint.exe", "그림판 (Paint)", ["paint", "mspaint"]),
            ("regedit.exe", "레지스트리 편집기 (regedit)", ["regedit", "registry"]),
            ("taskmgr.exe", "작업 관리자 (Task Manager)", ["taskmgr", "taskmanager"]),
            ("control.exe", "제어판 (Control Panel)", ["control", "controlpanel"]),
            ("snippingtool.exe", "캡처 도구 (Snipping Tool)", ["snip", "snipping", "snippingtool"]),
        ];

        foreach (var (exe, display, keys) in essentials)
        {
            var path = ResolveSystemPath(exe);
            yield return new InstalledApp
            {
                Name = display,
                LaunchUri = path ?? exe,
                IconSource = path,
                Keywords = [.. keys],
            };
        }
    }

    private static string? ResolveSystemPath(string exe)
    {
        var system32 = Path.Combine(Environment.SystemDirectory, exe);
        if (File.Exists(system32))
        {
            return system32;
        }

        var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", exe);
        return File.Exists(powerShell) ? powerShell : null;
    }
}
