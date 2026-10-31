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

/// <summary>A key combination as written in actions.json, e.g. "ctrl+alt+space".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
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

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (ModifierNames.TryGetValue(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }
            if (key is not null || ParseKey(part) is not { } vk)
                return false;
            key = vk;
        }

        // A hotkey without Ctrl, Alt or Win would fire while typing.
        if (key is null || (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
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
