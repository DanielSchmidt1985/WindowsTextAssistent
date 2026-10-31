namespace ClaudeHotkey.Core.Config;

/// <summary>actions.default.json, embedded in the assembly; copied to %APPDATA% on first start.</summary>
public static class DefaultConfig
{
    public static string Json()
    {
        using var stream = typeof(DefaultConfig).Assembly.GetManifestResourceStream("ClaudeHotkey.Core.Config.actions.default.json")
            ?? throw new InvalidOperationException("actions.default.json is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
