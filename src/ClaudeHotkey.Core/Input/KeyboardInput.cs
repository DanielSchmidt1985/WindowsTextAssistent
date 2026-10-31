using System.Runtime.InteropServices;
using static ClaudeHotkey.Core.Interop.NativeMethods;

namespace ClaudeHotkey.Core.Input;

/// <summary>Synthetic keyboard input, so text capture and pasting can be tested with a fake.</summary>
public interface IKeyboardInput
{
    /// <summary>Waits until no modifier (and not <paramref name="extraVirtualKey"/>) is physically held.</summary>
    Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, int? extraVirtualKey = null);

    /// <summary>Sends Ctrl + the given key (e.g. C or V) to the foreground window.</summary>
    void SendCtrlChord(int virtualKey);
}

public sealed class KeyboardInput : IKeyboardInput
{
    private static readonly int[] Modifiers = [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN];

    public async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, int? extraVirtualKey = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Modifiers.Any(IsKeyDown) || (extraVirtualKey is { } vk && IsKeyDown(vk)))
        {
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(15).ConfigureAwait(true);
        }
        return true;
    }

    public void SendCtrlChord(int virtualKey)
    {
        INPUT[] inputs =
        [
            Key(VK_CONTROL, keyUp: false),
            Key(virtualKey, keyUp: false),
            Key(virtualKey, keyUp: true),
            Key(VK_CONTROL, keyUp: true),
        ];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
    }

    private static INPUT Key(int virtualKey, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = (ushort)MapVirtualKey((uint)virtualKey, 0),
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
