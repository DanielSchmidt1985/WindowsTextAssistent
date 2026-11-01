using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Core.Config;

/// <summary>
/// actions.default.json (German) or actions.default-en.json, embedded in the assembly; copied to
/// %APPDATA% on first start in the UI language of that moment.
/// </summary>
public static class DefaultConfig
{
    public static string Json() => Json(Lang.Current);

    public static string Json(UiLanguage language)
    {
        var name = language == UiLanguage.German ? "actions.default.json" : "actions.default-en.json";
        using var stream = typeof(DefaultConfig).Assembly.GetManifestResourceStream($"ClaudeHotkey.Core.Config.{name}")
            ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
