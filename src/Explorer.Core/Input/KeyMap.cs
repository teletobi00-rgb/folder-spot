using System.Text.Json;

namespace Explorer.Core.Input;

file static class KeyMapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>키맵 액션 식별자. 뷰가 이 이름으로 명령을 해석한다.</summary>
public static class KeyActions
{
    public const string GoBack = "Nav.Back";
    public const string GoForward = "Nav.Forward";
    public const string GoUp = "Nav.Up";
    public const string Refresh = "Nav.Refresh";
    public const string NewTab = "Tabs.New";
    public const string CloseTab = "Tabs.Close";
    public const string NextTab = "Tabs.Next";
    public const string ToggleDualMode = "Workspace.ToggleDual";
    public const string SwitchPane = "Workspace.SwitchPane";
    public const string SwapPanes = "Workspace.SwapPanes";
    public const string CopyToOtherPane = "Workspace.CopyToOther";
    public const string MoveToOtherPane = "Workspace.MoveToOther";
    public const string OpenInOtherPane = "Workspace.OpenInOther";
    public const string Undo = "Edit.Undo";
    public const string ToggleQuickView = "Workspace.QuickView";

    /// <summary>전역(앱 밖) 핫키 — 윈도우 InputBindings가 아니라 RegisterHotKey로 처리된다.</summary>
    public const string GlobalSearch = "Global.Search";
}

/// <summary>
/// 액션 → 제스처 문자열("Ctrl+Shift+D", 복수는 ';' 구분) 중앙 키맵.
/// keymap.json의 사용자 오버라이드를 기본값 위에 병합한다 (Total Commander 키보드 계약이 기본).
/// </summary>
public sealed class KeyMap
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        [KeyActions.GoBack] = "Alt+Left",
        [KeyActions.GoForward] = "Alt+Right",
        [KeyActions.GoUp] = "Alt+Up",
        [KeyActions.Refresh] = "Ctrl+R",
        [KeyActions.NewTab] = "Ctrl+T",
        [KeyActions.CloseTab] = "Ctrl+W",
        [KeyActions.NextTab] = "Ctrl+Tab",
        [KeyActions.ToggleDualMode] = "Ctrl+Shift+D",
        [KeyActions.SwitchPane] = "Tab",
        [KeyActions.SwapPanes] = "Ctrl+U",
        [KeyActions.CopyToOtherPane] = "F5",
        [KeyActions.MoveToOtherPane] = "F6",
        [KeyActions.OpenInOtherPane] = "Ctrl+Right;Ctrl+Left",
        [KeyActions.Undo] = "Ctrl+Z",
        [KeyActions.ToggleQuickView] = "Ctrl+Q",

        // Win+Space는 OS가 IME 전환에 예약 — PowerToys Run과 같은 이유로 Alt+Space가 기본.
        [KeyActions.GlobalSearch] = "Alt+Space",
    };

    private KeyMap(Dictionary<string, string> bindings)
    {
        Bindings = bindings;
    }

    public IReadOnlyDictionary<string, string> Bindings { get; }

    public static KeyMap CreateDefault() => new(new Dictionary<string, string>(Defaults, StringComparer.Ordinal));

    /// <summary>
    /// 기본 키맵에 JSON 오버라이드 파일을 병합한다. 파일이 없거나 손상이면 기본값 그대로.
    /// 알 수 없는 액션이나 빈 제스처는 무시한다.
    /// </summary>
    public static KeyMap LoadWithOverrides(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var merged = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);
        if (!File.Exists(filePath))
        {
            return new KeyMap(merged);
        }

        try
        {
            var document = JsonSerializer.Deserialize<KeyMapDocument>(File.ReadAllText(filePath), KeyMapJson.Options);
            if (document?.Bindings is { } overrides)
            {
                foreach (var (action, gesture) in overrides)
                {
                    if (merged.ContainsKey(action) && !string.IsNullOrWhiteSpace(gesture))
                    {
                        merged[action] = gesture.Trim();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 키맵 파일 문제는 치명적이지 않다 — 기본값으로 동작한다.
            _ = ex;
        }

        return new KeyMap(merged);
    }

    public string GestureFor(string action) =>
        Bindings.TryGetValue(action, out var gesture) ? gesture : string.Empty;

    private sealed record KeyMapDocument
    {
        public int Version { get; init; } = 1;

        public Dictionary<string, string>? Bindings { get; init; }
    }
}
