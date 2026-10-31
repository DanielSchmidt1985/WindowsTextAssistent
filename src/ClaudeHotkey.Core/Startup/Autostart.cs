using Microsoft.Win32;

namespace ClaudeHotkey.Core.Startup;

/// <summary>Seam over HKCU\...\Run, so tests never touch the real registry.</summary>
public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

public sealed class CurrentUserRunKey : IRunKey
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// "Mit Windows starten": an entry under HKCU\Software\Microsoft\Windows\CurrentVersion\Run, only
/// while the setting is on. Per user, no admin rights needed.
/// </summary>
public sealed class Autostart(IRunKey runKey)
{
    public const string ValueName = "ClaudeHotkey";

    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    public bool IsEnabledFor(string exePath) =>
        string.Equals(runKey.Get(ValueName), CommandFor(exePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes the registry match the setting. Returns true when something was changed.</summary>
    public bool Apply(bool enabled, string exePath)
    {
        var current = runKey.Get(ValueName);
        if (enabled)
        {
            var wanted = CommandFor(exePath);
            if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                return false;
            runKey.Set(ValueName, wanted); // also fixes an entry that points to an old location
            return true;
        }
        if (current is null)
            return false;
        runKey.Delete(ValueName);
        return true;
    }
}
