using ClaudeHotkey.Core.Config;

namespace ClaudeHotkey.Tests;

public class ActionIdsTests
{
    [Theory]
    [InlineData("Höflicher", "hoeflicher")]
    [InlineData("Grüße & Gruß", "gruesse-gruss")]
    [InlineData("  In Stichpunkte!  ", "in-stichpunkte")]
    [InlineData("E-Mail (kurz)", "e-mail-kurz")]
    [InlineData("😀", "aktion")]
    [InlineData("", "aktion")]
    public void Builds_readable_ids(string label, string expected)
    {
        Assert.Equal(expected, ActionIds.FromLabel(label, []));
    }

    [Fact]
    public void Adds_suffix_when_taken()
    {
        Assert.Equal("kuerzen-3", ActionIds.FromLabel("Kürzen", ["kuerzen", "KUERZEN-2"]));
    }

    [Fact]
    public void Detailed_errors_name_action_and_field()
    {
        var config = new ActionConfig
        {
            Actions =
            [
                new ActionDefinition { Id = "a", Label = "A", Prompt = "P" },
                new ActionDefinition { Id = "b", Label = "B", Prompt = " ", Hotkey = "ctrl+c+c" },
            ],
        };
        config.Settings.TimeoutSeconds = 1;

        var errors = config.ValidateDetailed();

        Assert.Contains(errors, e => e is { ActionIndex: null, Field: "timeoutSeconds" });
        Assert.Contains(errors, e => e is { ActionIndex: 1, Field: "prompt" });
        Assert.Contains(errors, e => e is { ActionIndex: 1, Field: "hotkey" } && e.FieldMessage.Contains("Haupt-Hotkey"));
        Assert.DoesNotContain(errors, e => e.ActionIndex == 0);
    }
}
