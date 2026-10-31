using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.ViewModels;

namespace ClaudeHotkey.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("claudehotkey-settings");
    private readonly List<ClaudeRequest> _requests = [];

    public void Dispose() => _dir.Delete(recursive: true);

    private (SettingsViewModel ViewModel, ActionConfigStore Store) Create()
    {
        var store = new ActionConfigStore(_dir.FullName, DefaultConfig.Json);
        store.Reload();
        var viewModel = new SettingsViewModel(store, (request, _) =>
        {
            _requests.Add(request);
            return Task.FromResult(new ClaudeResult(ClaudeErrorKind.None, "Antwort", null, null));
        });
        return (viewModel, store);
    }

    [Fact]
    public void Loads_current_config()
    {
        var (vm, _) = Create();

        Assert.Equal(8, vm.Actions.Count);
        Assert.Equal("translate", vm.Selected!.Id);
        Assert.Equal("400", vm.DoubleCopyMs);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void New_action_is_saved_and_appears_in_config()
    {
        var (vm, store) = Create();
        ActionConfig? saved = null;
        vm.Saved += (_, config) => saved = config;

        vm.NewCommand.Execute(null);
        vm.Selected!.Label = "In Stichpunkte";
        vm.Selected.Prompt = "Fasse den Text in Stichpunkten zusammen.";
        Assert.True(vm.IsDirty);

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.NotNull(saved);
        var action = Assert.Single(store.Current.Actions, a => a.Id == "in-stichpunkte");
        Assert.Equal("In Stichpunkte", action.Label);
        Assert.Contains("in-stichpunkte", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Id_stays_stable_after_saving()
    {
        var (vm, _) = Create();
        vm.NewCommand.Execute(null);
        vm.Selected!.Label = "Erster Name";
        vm.Selected.Prompt = "P";
        vm.Save();

        vm.Selected.Label = "Anderer Name";

        Assert.Equal("erster-name", vm.Selected.Id);
    }

    [Fact]
    public void Existing_ids_never_change_when_renamed()
    {
        var (vm, _) = Create();

        vm.Selected!.Label = "Translate anders";

        Assert.Equal("translate", vm.Selected.Id);
    }

    [Fact]
    public void Duplicate_gets_own_id_and_no_hotkey()
    {
        var (vm, _) = Create();
        vm.Selected!.Hotkey = "ctrl+alt+u";

        vm.DuplicateCommand.Execute(null);

        Assert.Equal(9, vm.Actions.Count);
        Assert.Equal(1, vm.Actions.IndexOf(vm.Selected!));
        Assert.Equal("Übersetzen (Kopie)", vm.Selected!.Label);
        Assert.Equal("uebersetzen-kopie", vm.Selected.Id);
        Assert.Equal("", vm.Selected.Hotkey);
        Assert.True(vm.Save());
    }

    [Fact]
    public void Move_changes_popup_order()
    {
        var (vm, store) = Create();

        vm.Move(7, 0); // "Freitext" to the front
        vm.Save();

        Assert.Equal("free", store.Current.Actions[0].Id);
        Assert.Equal("translate", store.Current.Actions[1].Id);
    }

    [Fact]
    public void Delete_selects_neighbour_and_saves()
    {
        var (vm, store) = Create();
        vm.Selected = vm.Actions[7];

        vm.DeleteCommand.Execute(null);
        vm.Save();

        Assert.Equal(7, store.Current.Actions.Count);
        Assert.Equal("summarize", vm.Selected!.Id);
        Assert.DoesNotContain(store.Current.Actions, a => a.Id == "free");
    }

    [Fact]
    public void Errors_are_shown_at_fields_and_file_is_untouched()
    {
        var (vm, store) = Create();
        var before = File.ReadAllText(store.FilePath);
        vm.NewCommand.Execute(null);
        var broken = vm.Selected!;
        broken.Hotkey = "ctrl+alt+space";
        vm.Selected = vm.Actions[0];

        Assert.False(vm.Save());

        Assert.Equal(before, File.ReadAllText(store.FilePath));
        Assert.Same(broken, vm.Selected); // jumps to the action with errors
        Assert.NotNull(broken.Errors["prompt"]);
        Assert.Contains("zweiten Hotkey", broken.Errors["hotkey"]);
        Assert.Null(vm.Actions[0].Errors["prompt"]);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public void Non_numeric_general_field_is_an_error()
    {
        var (vm, _) = Create();
        vm.TimeoutSeconds = "sechzig";

        Assert.False(vm.Save());

        Assert.Equal("Bitte eine ganze Zahl eingeben.", vm.GeneralErrors["timeoutSeconds"]);
    }

    [Fact]
    public void Errors_disappear_after_fixing_and_saving()
    {
        var (vm, _) = Create();
        vm.DoubleCopyMs = "5";
        vm.Save();
        Assert.NotNull(vm.GeneralErrors["doubleCopyMs"]);

        vm.DoubleCopyMs = "500";

        Assert.True(vm.Save());
        Assert.Null(vm.GeneralErrors["doubleCopyMs"]);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void Empty_second_hotkey_is_saved()
    {
        var (vm, store) = Create();
        vm.SecondHotkey = "";

        Assert.True(vm.Save());
        Assert.Equal("", store.Current.Settings.SecondHotkey);
        Assert.Null(vm.GeneralErrors["secondHotkey"]);
    }

    [Fact]
    public void Double_press_hotkey_is_saved()
    {
        var (vm, store) = Create();
        vm.Selected!.Hotkey = "ctrl+x+x";

        Assert.True(vm.Save());
        Assert.Equal("ctrl+x+x", store.Current.Actions[0].Hotkey);
    }

    [Theory]
    [InlineData(null, 0, "ctrl+x", 100, "ctrl+x")]           // first press
    [InlineData("ctrl+x", 100, "ctrl+x", 400, "ctrl+x+x")]   // second press in time
    [InlineData("ctrl+x", 100, "ctrl+x", 900, "ctrl+x")]     // too slow
    [InlineData("ctrl+x", 100, "ctrl+y", 200, "ctrl+y")]     // other key
    [InlineData("ctrl+alt+f2", 0, "ctrl+alt+f2", 100, "ctrl+alt+f2+f2")]
    public void Hotkey_box_turns_quick_repeat_into_double_press(string? previous, long previousMs, string combo, long nowMs, string expected)
    {
        Assert.Equal(expected, ClaudeHotkey.Views.HotkeyCaptureRules.Combine(previous, previousMs, combo, nowMs));
    }

    [Fact]
    public async Task Test_uses_unsaved_prompt_and_system_prompt()
    {
        var (vm, _) = Create();
        vm.SystemPrompt = "Neuer System-Prompt";
        var action = vm.Selected!;
        action.Prompt = "Geänderter Prompt";
        action.Model = "sonnet";
        action.TestText = "Beispiel";

        action.TestCommand.Execute(null);
        await Task.Delay(50);

        var request = Assert.Single(_requests);
        Assert.Equal("Geänderter Prompt", request.ActionPrompt);
        Assert.Equal("Neuer System-Prompt", request.SystemPrompt);
        Assert.Equal("sonnet", request.Model);
        Assert.Equal("Beispiel", request.Text);
        Assert.Equal("Antwort", action.TestResult);
    }

    [Fact]
    public async Task Test_fills_input_placeholder()
    {
        var (vm, _) = Create();
        var free = vm.Actions.Single(a => a.Id == "free");
        free.TestInput = "Mach ein Gedicht daraus";

        free.TestCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal("Mach ein Gedicht daraus", Assert.Single(_requests).ActionPrompt);
    }
}
