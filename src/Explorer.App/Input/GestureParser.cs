using System.Windows.Input;

namespace Explorer.App.Input;

/// <summary>
/// 키맵의 제스처 문자열("Ctrl+Shift+D", 복수는 ';' 구분)을 WPF Key/Modifier로 변환한다.
/// KeyGestureConverter와 달리 수정자 없는 일반 키(Tab 등)도 허용한다.
/// </summary>
public static class GestureParser
{
    public static IReadOnlyList<(ModifierKeys Modifiers, Key Key)> Parse(string? gestures)
    {
        if (string.IsNullOrWhiteSpace(gestures))
        {
            return [];
        }

        var results = new List<(ModifierKeys, Key)>();
        foreach (var part in gestures.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseSingle(part, out var modifiers, out var key))
            {
                results.Add((modifiers, key));
            }
        }

        return results;
    }

    private static bool TryParseSingle(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        return TryParseKey(tokens[^1], out key);
    }

    private static bool TryParseKey(string token, out Key key)
    {
        var normalized = token.ToUpperInvariant() switch
        {
            "ESC" => "Escape",
            "DEL" => "Delete",
            "BACKSPACE" => "Back",
            "ENTER" => "Return",
            "PLUS" => "OemPlus",
            "MINUS" => "OemMinus",
            var digit when digit.Length == 1 && char.IsAsciiDigit(digit[0]) => "D" + digit,
            _ => token,
        };

        return Enum.TryParse(normalized, ignoreCase: true, out key) && key != Key.None;
    }
}
