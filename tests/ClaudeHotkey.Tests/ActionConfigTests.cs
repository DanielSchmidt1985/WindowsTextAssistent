using ClaudeHotkey.Core.Config;

namespace ClaudeHotkey.Tests;

public class ActionConfigTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("claudehotkey-config");

    public void Dispose() => _dir.Delete(recursive: true);

    private ActionConfigStore Store() => new(_dir.FullName, DefaultConfig.Json);

    private static string Json(string actions, string settings = "") =>
        $$"""{ "settings": { {{settings}} }, "actions": [ {{actions}} ] }""";

    [Fact]
    public void Default_config_is_valid_and_complete()
    {
        var config = ActionConfig.Parse(DefaultConfig.Json());

        Assert.Equal(["translate", "improve", "fix", "shorter", "friendly", "formal", "summarize", "free"], config.Actions.Select(a => a.Id));
        Assert.Equal("haiku", config.Settings.Model);
        Assert.Equal(400, config.Settings.DoubleCopyMs);
        Assert.Contains("<text>", config.Settings.SystemPrompt);
        Assert.False(config.Actions.Single(a => a.Id == "summarize").Replace);
        Assert.True(config.Actions.Single(a => a.Id == "free").AskInput);
        Assert.False(config.Settings.Autostart); // a new machine has no Run entry yet
    }

    [Fact]
    public void Default_config_carries_the_owners_setup()
    {
        var config = ActionConfig.Parse(DefaultConfig.Json());
        string? Hotkey(string id) => config.Actions.Single(a => a.Id == id).Hotkey;
        var fix = config.Actions.Single(a => a.Id == "fix");

        Assert.Equal("ctrl+x+x", Hotkey("translate"));
        Assert.Equal("alt+v+v", Hotkey("improve"));
        Assert.Equal("alt+k+k", Hotkey("fix"));
        Assert.Equal("sonnet", fix.EffectiveModel(config.Settings));
        Assert.Contains("Coaching", fix.Feedback);
        Assert.Equal("haiku", config.Actions.Single(a => a.Id == "shorter").EffectiveModel(config.Settings));
    }

    [Fact]
    public void Missing_fields_get_defaults()
    {
        var config = ActionConfig.Parse("""{ "actions": [ { "id": "a", "label": "A", "prompt": "P" } ] }""");

        var action = Assert.Single(config.Actions);
        Assert.True(action.Replace);
        Assert.True(action.Enabled);
        Assert.False(action.AskInput);
        Assert.Equal("haiku", action.EffectiveModel(config.Settings));
        Assert.Equal(60, config.Settings.TimeoutSeconds);
        Assert.Equal("ctrl+c+c", config.Settings.MainHotkey);
    }

    [Fact]
    public void Old_file_with_second_hotkey_still_loads()
    {
        // actions.json written before the main hotkey became configurable.
        var config = ActionConfig.Parse(Json("""{ "id": "a", "label": "A", "prompt": "P" }""", "\"secondHotkey\": \"ctrl+alt+space\""));

        Assert.Equal("ctrl+c+c", config.Settings.MainHotkey);
        Assert.DoesNotContain("secondHotkey", config.ToJson()); // gone after the next save
    }

    [Fact]
    public void Action_model_overrides_default()
    {
        var config = ActionConfig.Parse(Json("""{ "id": "a", "label": "A", "prompt": "P", "model": "sonnet" }"""));

        Assert.Equal("sonnet", config.Actions[0].EffectiveModel(config.Settings));
    }

    [Fact]
    public void Input_placeholder_is_replaced()
    {
        var action = new ActionDefinition { Prompt = "Mach daraus: {input}." };

        Assert.Equal("Mach daraus: ein Gedicht.", action.BuildPrompt("  ein Gedicht "));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var config = ActionConfig.Parse("""
            {
              // eigene Aktionen
              "actions": [ { "id": "a", "label": "A", "prompt": "P", }, ],
            }
            """);

        Assert.Single(config.Actions);
    }

    [Theory]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P" }, { "id": "A", "label": "B", "prompt": "Q" }""", "doppelt")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "  " }""", "prompt ist leer")]
    [InlineData("""{ "id": "", "label": "A", "prompt": "P" }""", "id fehlt")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "askInput": true }""", "{input}")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "shift+x" }""", "keine gültige")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+alt+1" }, { "id": "b", "label": "B", "prompt": "Q", "hotkey": "Strg+Alt+1" }""", "schon für „A“")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+c+c" }""", "schon für den Haupt-Hotkey")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+c" }""", "normales Kopieren")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+v" }""", "normales Kopieren")]
    [InlineData("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+alt+k" }, { "id": "b", "label": "B", "prompt": "Q", "hotkey": "ctrl+alt+k+k" }""", "schon für „A“")]
    public void Invalid_actions_are_rejected(string actions, string expected)
    {
        var ex = Assert.Throws<ActionConfigException>(() => ActionConfig.Parse(Json(actions)));

        Assert.Contains(ex.Errors, e => e.Contains(expected));
    }

    [Theory]
    [InlineData("\"doubleCopyMs\": 50", "doubleCopyMs")]
    [InlineData("\"timeoutSeconds\": 0", "timeoutSeconds")]
    [InlineData("\"mainHotkey\": \"x\"", "mainHotkey")]
    [InlineData("\"mainHotkey\": \"\"", "Ohne Haupt-Hotkey")]
    [InlineData("\"mainHotkey\": \"ctrl+c\"", "normales Kopieren")]
    [InlineData("\"model\": \"\"", "model")]
    public void Invalid_settings_are_rejected(string settings, string expected)
    {
        var ex = Assert.Throws<ActionConfigException>(() => ActionConfig.Parse(Json("", settings)));

        Assert.Contains(ex.Errors, e => e.Contains(expected));
    }

    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("ctrl+x+x")]
    [InlineData("ctrl+alt+c+c")]
    public void Main_hotkey_can_be_changed(string mainHotkey)
    {
        var config = ActionConfig.Parse(Json("", $"\"mainHotkey\": \"{mainHotkey}\""));

        Assert.Equal(mainHotkey, config.Settings.MainHotkey);
    }

    [Fact]
    public void Ctrl_c_c_is_free_for_an_action_when_main_hotkey_moved()
    {
        var config = ActionConfig.Parse(Json("""{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+c+c" }""",
            "\"mainHotkey\": \"ctrl+alt+space\""));

        Assert.Equal("ctrl+c+c", config.Actions[0].Hotkey);
    }

    [Fact]
    public void Action_hotkey_on_main_hotkey_is_rejected()
    {
        var ex = Assert.Throws<ActionConfigException>(() => ActionConfig.Parse(Json(
            """{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+alt+space" }""", "\"mainHotkey\": \"ctrl+alt+space\"")));

        Assert.Contains(ex.Errors, e => e.Contains("schon für den Haupt-Hotkey"));
    }

    [Fact]
    public void Double_press_hotkeys_are_accepted()
    {
        var config = ActionConfig.Parse(Json(
            """{ "id": "a", "label": "A", "prompt": "P", "hotkey": "ctrl+x+x" }, { "id": "b", "label": "B", "prompt": "Q", "hotkey": "ctrl+alt+q+q" }"""));

        Assert.Equal(2, config.Actions.Count);
    }

    [Fact]
    public void Broken_json_names_the_line()
    {
        var ex = Assert.Throws<ActionConfigException>(() => ActionConfig.Parse("{\n  \"actions\": [\n    { \"id\": }\n  ]\n}"));

        Assert.Contains("Zeile 3", ex.Errors[0]);
    }

    [Fact]
    public void First_start_creates_file_from_defaults()
    {
        var store = Store();

        Assert.True(store.Reload());
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(8, store.Current.Actions.Count);
    }

    [Fact]
    public void Broken_file_keeps_last_valid_config_and_reports()
    {
        var store = Store();
        store.Reload();
        File.WriteAllText(store.FilePath, "{ kaputt");
        File.SetLastWriteTimeUtc(store.FilePath, DateTime.UtcNow.AddSeconds(5));

        Assert.False(store.Reload());
        Assert.Equal(8, store.Current.Actions.Count);
        Assert.NotEmpty(store.LoadErrors);
        Assert.False(store.Reload()); // still broken, still reported
    }

    [Fact]
    public void Fixed_file_is_picked_up_again()
    {
        var store = Store();
        store.Reload();
        File.WriteAllText(store.FilePath, "{ kaputt");
        File.SetLastWriteTimeUtc(store.FilePath, DateTime.UtcNow.AddSeconds(5));
        store.Reload();

        File.WriteAllText(store.FilePath, Json("""{ "id": "neu", "label": "Neu", "prompt": "P" }"""));
        File.SetLastWriteTimeUtc(store.FilePath, DateTime.UtcNow.AddSeconds(10));

        Assert.True(store.Reload());
        Assert.Empty(store.LoadErrors);
        Assert.Equal("neu", Assert.Single(store.Current.Actions).Id);
    }

    [Fact]
    public void Save_round_trips_and_keeps_umlauts_readable()
    {
        var store = Store();
        store.Reload();
        var config = store.Current;
        config.Actions.Add(new ActionDefinition { Id = "x", Label = "Grüßen", Prompt = "Grüß schön.", Model = "sonnet", Hotkey = "ctrl+alt+g" });

        store.Save(config);

        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("Grüßen", text);
        Assert.Contains("<text>", text);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
        var reloaded = ActionConfig.Parse(text);
        Assert.Equal("sonnet", reloaded.Actions.Single(a => a.Id == "x").Model);
        Assert.Equal("ctrl+alt+g", reloaded.Actions.Single(a => a.Id == "x").Hotkey);
    }

    [Fact]
    public void Save_rejects_invalid_config_without_touching_file()
    {
        var store = Store();
        store.Reload();
        var before = File.ReadAllText(store.FilePath);
        var config = ActionConfig.Parse(before);
        config.Actions.Add(new ActionDefinition { Id = "translate", Label = "Doppelt", Prompt = "P" });

        Assert.Throws<ActionConfigException>(() => store.Save(config));
        Assert.Equal(before, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Disabled_actions_are_hidden()
    {
        var config = ActionConfig.Parse(Json("""{ "id": "a", "label": "A", "prompt": "P", "enabled": false }, { "id": "b", "label": "B", "prompt": "Q" }"""));

        Assert.Equal(["b"], config.EnabledActions.Select(a => a.Id));
    }
}
