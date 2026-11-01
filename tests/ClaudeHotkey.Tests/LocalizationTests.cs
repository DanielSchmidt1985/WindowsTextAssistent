using System.Globalization;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("de-DE", UiLanguage.German)]
    [InlineData("de-AT", UiLanguage.German)]
    [InlineData("en-US", UiLanguage.English)]
    [InlineData("fr-FR", UiLanguage.English)] // every other language falls back to English
    public void Display_language_decides_for_system(string culture, UiLanguage expected)
    {
        var display = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(expected, Lang.Resolve("system", display));
        Assert.Equal(expected, Lang.Resolve(null, display));
        Assert.Equal(expected, Lang.Resolve("", display));
    }

    [Fact]
    public void Explicit_setting_wins_over_the_display_language()
    {
        Assert.Equal(UiLanguage.German, Lang.Resolve("de", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(UiLanguage.English, Lang.Resolve(" EN ", CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void English_default_config_has_the_same_actions_as_the_german_one()
    {
        var german = ActionConfig.Parse(DefaultConfig.Json(UiLanguage.German));
        var english = ActionConfig.Parse(DefaultConfig.Json(UiLanguage.English));

        string Shape(ActionDefinition a) => $"{a.Id}|{a.Hotkey}|{a.Model}|{a.Replace}|{a.AskInput}|{a.Feedback is null}";
        Assert.Equal(german.Actions.Select(Shape), english.Actions.Select(Shape));
        Assert.Equal("Translate", english.Actions[0].Label);
        Assert.Contains("<text>", english.Settings.SystemPrompt);
        Assert.Equal("{input}", english.Actions.Single(a => a.Id == "free").Prompt);
        Assert.DoesNotMatch("[äöüß„]", DefaultConfig.Json(UiLanguage.English));
    }

    [Fact]
    public void Old_config_without_language_and_theme_follows_windows()
    {
        var config = ActionConfig.Parse("""{ "settings": {}, "actions": [] }""");

        Assert.Equal("system", config.Settings.Language);
        Assert.Equal("system", config.Settings.Theme);
    }

    [Fact]
    public void Unknown_language_or_theme_is_rejected()
    {
        var config = ActionConfig.Parse("""{ "settings": {}, "actions": [] }""");
        config.Settings.Language = "fr";
        config.Settings.Theme = "blue";

        var fields = config.ValidateDetailed().Select(e => e.Field);

        Assert.Equal(["language", "theme"], fields);
    }

    [Theory]
    [InlineData("system", true, true)]
    [InlineData("system", false, false)]
    [InlineData("", true, true)]
    [InlineData("light", false, true)]
    [InlineData("dark", true, false)]
    [InlineData("Light", false, true)]
    public void Theme_setting_or_windows_app_mode_picks_the_palette(string setting, bool windowsLight, bool expectedLight)
    {
        Assert.Equal(expectedLight, ThemeSetting.IsLight(setting, windowsLight));
    }
}
