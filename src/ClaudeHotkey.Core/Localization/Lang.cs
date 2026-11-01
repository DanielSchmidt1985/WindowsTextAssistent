using System.Globalization;

namespace ClaudeHotkey.Core.Localization;

public enum UiLanguage
{
    German,
    English,
}

/// <summary>
/// The UI language. Only German and English exist, so texts sit next to each other at the call
/// site (<c>Lang.T("Abbrechen", "Cancel")</c>) instead of in resource files. Set once at startup
/// from the setting and the Windows display language; windows opened later use the new value.
/// </summary>
public static class Lang
{
    /// <summary>Values of <c>settings.language</c> in actions.json.</summary>
    public const string System = "system", German = "de", English = "en";

    public static UiLanguage Current { get; set; } = FromCulture(CultureInfo.CurrentUICulture);

    public static bool IsGerman => Current == UiLanguage.German;

    public static string T(string german, string english) => IsGerman ? german : english;

    /// <summary>German for a German display language, English for every other.</summary>
    public static UiLanguage FromCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "de" ? UiLanguage.German : UiLanguage.English;

    /// <param name="setting">"de", "en", or "system"/empty for the Windows display language.</param>
    public static UiLanguage Resolve(string? setting, CultureInfo displayCulture) => setting?.Trim().ToLowerInvariant() switch
    {
        German => UiLanguage.German,
        English => UiLanguage.English,
        _ => FromCulture(displayCulture),
    };

    public static bool IsValidSetting(string? setting) =>
        string.IsNullOrWhiteSpace(setting) || setting.Trim().ToLowerInvariant() is System or German or English;
}
