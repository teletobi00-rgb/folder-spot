using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Explorer.App.Input;

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>
    /// 제스처 문자열("Alt+Space", 복수는 ';')로 전역 핫키를 등록한다.
    /// 하나라도 실패하면 false (다른 앱 선점 등) — 성공한 것은 유지된다. UI 스레드에서 호출.
    /// </summary>
    bool TryRegister(string gesture, Action callback);
}

/// <summary>RegisterHotKey + 메시지 전용 윈도우(WM_HOTKEY) 기반 전역 핫키.</summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly Dictionary<int, Action> _callbacks = [];
    private HwndSource? _messageWindow;
    private int _nextId = 1;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public bool TryRegister(string gesture, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var parsed = GestureParser.Parse(gesture);
        if (parsed.Count == 0)
        {
            _logger.LogWarning("전역 핫키 제스처를 해석할 수 없습니다: {Gesture}", gesture);
            return false;
        }

        EnsureMessageWindow();
        var allSucceeded = true;
        foreach (var (modifiers, key) in parsed)
        {
            var id = _nextId++;
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (RegisterHotKey(_messageWindow!.Handle, id, ToNativeModifiers(modifiers) | ModNoRepeat, virtualKey))
            {
                _callbacks[id] = callback;
                _logger.LogDebug("전역 핫키 등록: {Modifiers}+{Key}", modifiers, key);
            }
            else
            {
                allSucceeded = false;
                _logger.LogWarning("전역 핫키 등록 실패(다른 앱 선점 가능): {Modifiers}+{Key}", modifiers, key);
            }
        }

        return allSucceeded;
    }

    public void Dispose()
    {
        if (_messageWindow is { } window)
        {
            foreach (var id in _callbacks.Keys)
            {
                _ = UnregisterHotKey(window.Handle, id);
            }

            window.RemoveHook(WndProc);
            window.Dispose();
            _messageWindow = null;
        }

        _callbacks.Clear();
    }

    private void EnsureMessageWindow()
    {
        if (_messageWindow is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("Explorer.Hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — 메시지 전용 윈도우
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && _callbacks.TryGetValue((int)wParam, out var callback))
        {
            handled = true;
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "전역 핫키 콜백 실패");
            }
        }

        return 0;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint native = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            native |= 0x1;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            native |= 0x2;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            native |= 0x4;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            native |= 0x8;
        }

        return native;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
