using System.Diagnostics.CodeAnalysis;
using static ClaudeHotkey.Core.Interop.NativeMethods;

namespace ClaudeHotkey.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = MOD_ALT,
    Ctrl = MOD_CONTROL,
    Shift = MOD_SHIFT,
    Win = MOD_WIN,
}

/// <summary>
/// A key combination as written in actions.json: "ctrl+alt+space", or with the key twice for a
/// double press, "ctrl+x+x" (modifiers held, key pressed twice in quick succession). Single
/// presses go through RegisterHotKey, double presses through the keyboard hook.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey, bool IsDoublePress = false)
{
    /// <summary>Ctrl+C+C, the default main hotkey.</summary>
    public static readonly HotkeyGesture DoubleCopy = new(HotkeyModifiers.Ctrl, 'C', IsDoublePress: true);

    /// <summary>
    /// Ctrl+C+C or Ctrl+X+X: the app copies (or cuts) the selection itself on the first press,
    /// so the tool takes it from the clipboard. For every other gesture the tool sends Ctrl+C.
    /// </summary>
    public bool AppCopiesItself => IsDoublePress && Modifiers == HotkeyModifiers.Ctrl && VirtualKey is 'C' or 'X';

    /// <summary>
    /// Ctrl+C, Ctrl+X or Ctrl+V as a single press: RegisterHotKey would take the key away from
    /// every other program, so copying, cutting or pasting would stop working.
    /// </summary>
    public bool BlocksClipboardKeys => !IsDoublePress && Modifiers == HotkeyModifiers.Ctrl && VirtualKey is 'C' or 'X' or 'V';

    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = HotkeyModifiers.Ctrl, ["control"] = HotkeyModifiers.Ctrl, ["strg"] = HotkeyModifiers.Ctrl,
        ["alt"] = HotkeyModifiers.Alt,
        ["shift"] = HotkeyModifiers.Shift, ["umschalt"] = HotkeyModifiers.Shift,
        ["win"] = HotkeyModifiers.Win,
    };

    private static readonly Dictionary<string, int> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20, ["leertaste"] = 0x20,
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["insert"] = 0x2D, ["einfg"] = 0x2D,
        ["delete"] = 0x2E, ["entf"] = 0x2E,
        ["home"] = 0x24, ["pos1"] = 0x24,
        ["end"] = 0x23, ["ende"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["pause"] = 0x13,
    };

    /// <summary>Same modifiers and key, single or double: both cannot be used at the same time.</summary>
    public bool SharesKeysWith(HotkeyGesture other) => Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        var keys = new List<int>();
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (ModifierNames.TryGetValue(part, out var modifier))
            {
                if (keys.Count > 0)
                    return false; // modifiers come first
                modifiers |= modifier;
                continue;
            }
            if (ParseKey(part) is not { } vk)
                return false;
            keys.Add(vk);
        }

        // One key, or the same key twice for a double press.
        var isDouble = keys.Count == 2 && keys[0] == keys[1];
        if (keys.Count is 0 or > 2 || (keys.Count == 2 && !isDouble))
            return false;
        // Without Ctrl, Alt or Win the hotkey would fire while typing.
        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return false;
        gesture = new HotkeyGesture(modifiers, keys[0], isDouble);
        return true;
    }

    private static int? ParseKey(string part)
    {
        if (KeyNames.TryGetValue(part, out var named))
            return named;
        if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
            return char.ToUpperInvariant(part[0]);
        if (part.Length is 2 or 3 && (part[0] is 'f' or 'F') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24)
            return 0x70 + n - 1;
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("win");
        parts.Add(KeyName(VirtualKey));
        if (IsDoublePress)
            parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    private static string KeyName(int vk)
    {
        if (vk is >= 0x70 and <= 0x87)
            return $"f{vk - 0x70 + 1}";
        if (vk is >= '0' and <= '9' or >= 'A' and <= 'Z')
            return ((char)vk).ToString().ToLowerInvariant();
        return KeyNames.First(pair => pair.Value == vk && pair.Key.All(char.IsAsciiLetterLower)).Key;
    }
}
