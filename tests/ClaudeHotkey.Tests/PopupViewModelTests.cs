using ClaudeHotkey.Core.Claude;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.ViewModels;

namespace ClaudeHotkey.Tests;

public class PopupViewModelTests
{
    private readonly List<ClaudeRequest> _requests = [];
    private readonly Queue<ClaudeResult> _answers = new();

    /// <summary>Answers synchronously, so the fire-and-forget commands are done when Execute returns.</summary>
    private PopupViewModel Create(string text = "Hallo Herr Bauer")
    {
        var config = ActionConfig.Parse(DefaultConfig.Json());
        return new PopupViewModel(text, null, config, (request, _, _) =>
        {
            _requests.Add(request);
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : Ok("?"));
        }, _ => { });
    }

    private static ClaudeResult Ok(string text, string? feedback = null) => new(ClaudeErrorKind.None, text, null, null, feedback);

    private static ClaudeResult Failed() => new(ClaudeErrorKind.Failed, null, "kaputt", null);

    private void Answer(params ClaudeResult[] answers)
    {
        foreach (var answer in answers)
            _answers.Enqueue(answer);
    }

    private static void Run(PopupViewModel vm, string id) => vm.StartAction(id);

    private static void Refine(PopupViewModel vm, string instruction)
    {
        vm.RefineText = instruction;
        vm.RefineCommand.Execute(null);
    }

    [Fact]
    public void Refine_builds_on_the_current_result()
    {
        var vm = Create();
        Answer(Ok("Guten Tag Herr Bauer"), Ok("Hallo Bauer"));
        Run(vm, "improve");

        Refine(vm, "in Du-Form");

        Assert.Equal("Hallo Bauer", vm.ResultText);
        var request = _requests[1];
        Assert.Equal("Hallo Herr Bauer", request.Text);
        Assert.Equal("Guten Tag Herr Bauer", request.Refinement!.PreviousResult);
        Assert.Equal(["in Du-Form"], request.Refinement.Instructions);
        Assert.Equal(_requests[0].ActionPrompt, request.ActionPrompt);
        Assert.Equal(_requests[0].Model, request.Model);
        Assert.Equal("", vm.RefineText);
    }

    [Fact]
    public void Versions_can_be_browsed_and_replace_takes_the_shown_one()
    {
        var vm = Create();
        Answer(Ok("v1"), Ok("v2"));
        Run(vm, "improve");
        Refine(vm, "in Du-Form");
        string? replaced = null;
        vm.ReplaceRequested += (_, text) => replaced = text;

        Assert.Equal("2 / 2", vm.VersionInfo);
        Assert.Equal(["Verbessern", "in Du-Form"], vm.Steps.Select(s => s.Label));
        Assert.True(vm.Steps[1].IsCurrent);

        vm.PreviousVersionCommand.Execute(null);

        Assert.Equal("v1", vm.ResultText);
        Assert.Equal("1 / 2", vm.VersionInfo);
        Assert.True(vm.Steps[0].IsCurrent);
        Assert.False(vm.PreviousVersionCommand.CanExecute(null));
        vm.ReplaceCommand.Execute(null);
        Assert.Equal("v1", replaced);

        vm.SelectVersionCommand.Execute(vm.Steps[1]);
        Assert.Equal("v2", vm.ResultText);
    }

    [Fact]
    public void Refining_an_older_version_drops_the_later_ones()
    {
        var vm = Create();
        Answer(Ok("v1"), Ok("v2"), Ok("v3"), Ok("v1b"));
        Run(vm, "improve");
        Refine(vm, "in Du-Form");
        Refine(vm, "kürzer");
        vm.SelectVersionCommand.Execute(vm.Steps[0]);

        Refine(vm, "freundlicher");

        Assert.Equal(["Verbessern", "freundlicher"], vm.Steps.Select(s => s.Label));
        Assert.Equal("v1", _requests[3].Refinement!.PreviousResult);
        Assert.Equal(["freundlicher"], _requests[3].Refinement!.Instructions);
    }

    [Fact]
    public void Earlier_wishes_are_passed_along()
    {
        var vm = Create();
        Answer(Ok("v1"), Ok("v2"), Ok("v3"));
        Run(vm, "improve");
        Refine(vm, "in Du-Form");

        Refine(vm, "kürzer");

        Assert.Equal(["in Du-Form", "kürzer"], _requests[2].Refinement!.Instructions);
        Assert.Equal("v2", _requests[2].Refinement!.PreviousResult);
    }

    [Fact]
    public void Feedback_belongs_to_its_version()
    {
        var vm = Create();
        Answer(Ok("v1", "Tipp 1"), Ok("v2", "Tipp 2"));
        Run(vm, "fix");
        Refine(vm, "in Du-Form");

        Assert.Equal("Tipp 2", vm.FeedbackText);
        Assert.NotNull(_requests[1].FeedbackInstruction);
        vm.PreviousVersionCommand.Execute(null);
        Assert.Equal("Tipp 1", vm.FeedbackText);
    }

    [Fact]
    public void Failed_refinement_keeps_the_versions_and_can_go_back_or_retry()
    {
        var vm = Create();
        Answer(Ok("v1"), Failed(), Ok("v2"));
        Run(vm, "improve");

        Refine(vm, "in Du-Form");

        Assert.True(vm.HasError);
        Assert.True(vm.BackToResultCommand.CanExecute(null));
        vm.BackToResultCommand.Execute(null);
        Assert.True(vm.HasResult);
        Assert.Equal("v1", vm.ResultText);
        Assert.Equal("in Du-Form", vm.RefineText);

        Refine(vm, "in Du-Form");
        Assert.False(vm.HasError);
        Assert.Equal("v2", vm.ResultText);

        // Retry after a failure runs the follow-up again, not the action from scratch.
        var vm2 = Create();
        Answer(Ok("w1"), Failed(), Ok("w2"));
        Run(vm2, "improve");
        Refine(vm2, "kürzer");
        vm2.RetryCommand.Execute(null);
        Assert.Equal("w2", vm2.ResultText);
        Assert.Equal(["kürzer"], _requests.Last().Refinement!.Instructions);
    }

    [Fact]
    public void A_new_action_starts_a_new_history()
    {
        var vm = Create();
        Answer(Ok("v1"), Ok("v2"), Ok("kurz"));
        Run(vm, "improve");
        Refine(vm, "in Du-Form");

        Run(vm, "shorter");

        Assert.Null(_requests[2].Refinement);
        Assert.Equal(["Kürzen"], vm.Steps.Select(s => s.Label));
        Assert.False(vm.HasVersions);
    }

    [Fact]
    public void Refine_needs_a_result_and_some_text()
    {
        var vm = Create();
        vm.RefineText = "in Du-Form";
        Assert.False(vm.RefineCommand.CanExecute(null));

        Answer(Ok("v1"));
        Run(vm, "improve");
        vm.RefineText = "  ";
        Assert.False(vm.RefineCommand.CanExecute(null));
        vm.RefineText = "in Du-Form";
        Assert.True(vm.RefineCommand.CanExecute(null));
    }
}
