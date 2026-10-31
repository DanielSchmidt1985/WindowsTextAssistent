using ClaudeHotkey.Core.Claude;

namespace ClaudeHotkey.Tests;

public class ClaudeCliTests
{
    private static readonly ClaudeExecutable Exe = new(@"C:\tools\claude.exe", IsCmdShim: false);

    private static ClaudeRequest Request(string text = "Hallo Welt", TimeSpan? timeout = null) =>
        new("Korrigiere den Text.", text, "haiku", "Antworte nur mit dem Ergebnis.", timeout ?? TimeSpan.FromSeconds(30));

    private static string ResultJson(string result, bool isError = false, string status = "null") =>
        $$"""{"type":"result","is_error":{{(isError ? "true" : "false")}},"api_error_status":{{status}},"result":{{System.Text.Json.JsonSerializer.Serialize(result)}}}""";

    private static (ClaudeCli Cli, FakeRunner Runner) Create(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> handler, ClaudeExecutable? exe = null)
    {
        var runner = new FakeRunner(handler);
        var resolved = exe ?? Exe;
        return (new ClaudeCli(runner, () => resolved, Path.Combine(Path.GetTempPath(), "ClaudeHotkeyTests")), runner);
    }

    private static Task<ProcessResult> Ok(string stdout, int exitCode = 0) => Task.FromResult(new ProcessResult(exitCode, stdout, ""));

    [Fact]
    public async Task Run_builds_expected_command()
    {
        var (cli, runner) = Create((_, _) => Ok(ResultJson("Hallo Welt!")));

        await cli.RunAsync(Request());

        var request = Assert.Single(runner.Requests);
        Assert.Equal(Exe.Path, request.FileName);
        Assert.Null(request.RawArguments);
        Assert.Equal(
            ["-p", "--model", "haiku", "--output-format", "json", "--system-prompt",
             "Antworte nur mit dem Ergebnis.\n\nAufgabe:\nKorrigiere den Text.",
             "--no-session-persistence", "--strict-mcp-config", "--tools", ""],
            request.Arguments);
        Assert.Equal("<text>\nHallo Welt\n</text>", request.StandardInput);
    }

    private static ClaudeRequest FeedbackRequest() =>
        new("Korrigiere den Text.", "I want make a meeting.", "haiku", "Antworte nur mit dem Ergebnis.", TimeSpan.FromSeconds(30),
            "Kurzes Coaching zu den Fehlern.");

    private static string StructuredJson(string result, string? structured) =>
        $$"""{"type":"result","is_error":false,"result":{{System.Text.Json.JsonSerializer.Serialize(result)}}{{(structured is null ? "" : $",\"structured_output\":{structured}")}}}""";

    [Fact]
    public async Task Feedback_adds_json_schema_and_answer_format()
    {
        var (cli, runner) = Create((_, _) => Ok(StructuredJson("", """{"text":"x","feedback":""}""")));

        await cli.RunAsync(FeedbackRequest());

        var arguments = runner.Requests[0].Arguments;
        var schemaIndex = arguments.ToList().IndexOf("--json-schema");
        Assert.True(schemaIndex > 0);
        Assert.Equal(ClaudeCli.FeedbackSchema, arguments[schemaIndex + 1]);
        Assert.Equal(["--tools", ""], arguments.TakeLast(2)); // --tools must stay last
        var systemPrompt = arguments[arguments.ToList().IndexOf("--system-prompt") + 1];
        Assert.Contains("„feedback“: Kurzes Coaching zu den Fehlern.", systemPrompt);
        Assert.Contains("ausschließlich in „feedback“", systemPrompt);
    }

    [Fact]
    public async Task Without_feedback_there_is_no_schema()
    {
        var (cli, runner) = Create((_, _) => Ok(ResultJson("x")));

        await cli.RunAsync(Request());

        Assert.DoesNotContain("--json-schema", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task Structured_output_is_split_into_text_and_feedback()
    {
        // Shape taken from a real run with --json-schema (Claude Code 2.1.240).
        var (cli, _) = Create((_, _) => Ok(StructuredJson(
            """{"text":"I want to make a meeting.","feedback":"Nach want kommt to."}""",
            """{"text":"I want to make a meeting.","feedback":"Nach want kommt to."}""")));

        var result = await cli.RunAsync(FeedbackRequest());

        Assert.True(result.Success);
        Assert.Equal("I want to make a meeting.", result.Text);
        Assert.Equal("Nach want kommt to.", result.Feedback);
    }

    [Fact]
    public async Task Empty_feedback_becomes_null()
    {
        var (cli, _) = Create((_, _) => Ok(StructuredJson("", """{"text":"Hallo Welt.","feedback":"  "}""")));

        var result = await cli.RunAsync(FeedbackRequest());

        Assert.Equal("Hallo Welt.", result.Text);
        Assert.Null(result.Feedback);
    }

    [Fact]
    public async Task Json_in_result_is_used_when_structured_output_is_missing()
    {
        var (cli, _) = Create((_, _) => Ok(StructuredJson("""{"text":"Korrigiert.","feedback":"Tipp."}""", null)));

        var result = await cli.RunAsync(FeedbackRequest());

        Assert.Equal("Korrigiert.", result.Text);
        Assert.Equal("Tipp.", result.Feedback);
    }

    [Fact]
    public async Task Plain_answer_counts_as_text_when_no_object_comes_back()
    {
        var (cli, _) = Create((_, _) => Ok(StructuredJson("Einfach nur Text.", null)));

        var result = await cli.RunAsync(FeedbackRequest());

        Assert.Equal("Einfach nur Text.", result.Text);
        Assert.Null(result.Feedback);
    }

    [Fact]
    public async Task Selected_text_never_appears_in_arguments()
    {
        var (cli, runner) = Create((_, _) => Ok(ResultJson("x")));

        await cli.RunAsync(Request("geheimer Text"));

        Assert.DoesNotContain(runner.Requests[0].Arguments, a => a.Contains("geheimer Text"));
    }

    [Fact]
    public async Task Api_key_variables_are_removed_from_environment()
    {
        var (cli, runner) = Create((_, _) => Ok(ResultJson("x")));

        await cli.RunAsync(Request());

        Assert.Contains("ANTHROPIC_API_KEY", runner.Requests[0].RemovedEnvironmentVariables);
        Assert.Contains("ANTHROPIC_AUTH_TOKEN", runner.Requests[0].RemovedEnvironmentVariables);
    }

    [Fact]
    public async Task Real_runner_does_not_pass_api_key_to_child()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test-not-real");
        try
        {
            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var request = new ProcessRequest(cmd, ["/d", "/c", "set ANTHROPIC"], null, Path.GetTempPath(), null, ClaudeCli.RemovedEnvironmentVariables);

            var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

            Assert.DoesNotContain("sk-test-not-real", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public void Inherited_claude_code_session_variables_are_removed()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDE_CODE_SIMPLE"] = "1",
            ["CLAUDE_CODE_ENTRYPOINT"] = "claude-desktop",
            ["CLAUDECODE"] = "1",
            ["ANTHROPIC_BASE_URL"] = "http://localhost:1234",
            ["CLAUDE_AGENT_SDK_VERSION"] = "1.0",
            ["PATH"] = @"C:\Windows",
            ["CLAUDE_CONFIG_DIR"] = @"C:\custom",
        };

        ProcessRunner.RemoveEnvironmentVariables(environment, ClaudeCli.RemovedEnvironmentVariables);

        Assert.Equal(["PATH", "CLAUDE_CONFIG_DIR"], environment.Keys.Order().Reverse());
    }

    [Fact]
    public async Task Successful_answer_is_returned_trimmed_and_unwrapped()
    {
        var (cli, _) = Create((_, _) => Ok(ResultJson("  <text>Hallo Welt!</text>\n")));

        var result = await cli.RunAsync(Request());

        Assert.True(result.Success);
        Assert.Equal("Hallo Welt!", result.Text);
    }

    [Fact]
    public async Task Missing_cli_reports_not_found()
    {
        var cli = new ClaudeCli(new FakeRunner((_, _) => Ok("")), () => null, Path.GetTempPath());

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.CliNotFound, result.Error);
        Assert.Contains("nicht gefunden", result.Message);
    }

    [Fact]
    public async Task Start_failure_reports_not_found()
    {
        var (cli, _) = Create((_, _) => throw new ProcessStartException("kaputt", new Exception()));

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.CliNotFound, result.Error);
    }

    [Fact]
    public async Task Expired_login_reports_not_logged_in()
    {
        // Shape taken from a real CLI run with an expired session.
        var (cli, _) = Create((_, _) => Ok(ResultJson("Failed to authenticate: OAuth session expired and could not be refreshed", isError: true), exitCode: 1));

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.NotLoggedIn, result.Error);
        Assert.Contains("angemeldet", result.Message);
        Assert.Contains("OAuth session expired", result.Details);
    }

    [Theory]
    [InlineData("Claude AI usage limit reached|1730000000", "null")]
    [InlineData("You've hit your limit · resets 5pm", "null")]
    [InlineData("Too many requests", "429")]
    public async Task Limit_reports_limit_reached(string message, string status)
    {
        var (cli, _) = Create((_, _) => Ok(ResultJson(message, isError: true, status: status), exitCode: 1));

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.LimitReached, result.Error);
    }

    [Fact]
    public async Task Empty_answer_reports_empty_response()
    {
        var (cli, _) = Create((_, _) => Ok(ResultJson("   ")));

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.EmptyResponse, result.Error);
    }

    [Fact]
    public async Task Non_json_output_reports_failure_with_details()
    {
        var (cli, _) = Create((_, _) => Task.FromResult(new ProcessResult(1, "", "Error: unknown option '--foo'")));

        var result = await cli.RunAsync(Request());

        Assert.Equal(ClaudeErrorKind.Failed, result.Error);
        Assert.Contains("unknown option", result.Details);
    }

    [Fact]
    public async Task Timeout_cancels_process_and_reports_timeout()
    {
        CancellationToken seen = default;
        var (cli, _) = Create(async (_, ct) =>
        {
            seen = ct;
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessResult(0, "", "");
        });

        var result = await cli.RunAsync(Request(timeout: TimeSpan.FromMilliseconds(50)));

        Assert.Equal(ClaudeErrorKind.Timeout, result.Error);
        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task User_cancel_reports_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var (cli, _) = Create(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessResult(0, "", "");
        });

        var result = await cli.RunAsync(Request(), cts.Token);

        Assert.Equal(ClaudeErrorKind.Cancelled, result.Error);
    }

    [Fact]
    public async Task Real_runner_kills_process_on_timeout()
    {
        // ping waits ~10 s; the runner must kill it well before that.
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var request = new ProcessRequest(cmd, ["/d", "/c", "ping -n 11 127.0.0.1 >nul"], null, Path.GetTempPath(), null, []);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(request, cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Real_runner_round_trips_utf8_through_stdin()
    {
        const string text = "Grüße, Ärger, ß – 😀\nzweite Zeile";
        var request = new ProcessRequest("powershell.exe",
            ["-NoProfile", "-Command", "[Console]::InputEncoding=[Console]::OutputEncoding=New-Object Text.UTF8Encoding $false; [Console]::Out.Write([Console]::In.ReadToEnd())"],
            null, Path.GetTempPath(), text, []);

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        Assert.Equal(text, result.StandardOutput);
    }

    [Fact]
    public async Task Cmd_shim_is_started_through_cmd_exe()
    {
        var shim = new ClaudeExecutable(@"C:\npm\claude.cmd", IsCmdShim: true);
        var (cli, runner) = Create((_, _) => Ok(ResultJson("x")), shim);

        await cli.RunAsync(Request());

        var request = runner.Requests[0];
        Assert.EndsWith("cmd.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/d /s /c \"", request.RawArguments);
        Assert.Contains("claude.cmd", request.RawArguments);
    }

    [Fact]
    public async Task Cmd_shim_passes_special_characters_intact()
    {
        // A fake .cmd shim that forwards its arguments to PowerShell, which prints them one per line.
        var dir = Directory.CreateTempSubdirectory("claudehotkey-shim");
        try
        {
            var script = Path.Combine(dir.FullName, "echo.ps1");
            File.WriteAllText(script, "[Console]::OutputEncoding=New-Object Text.UTF8Encoding $false; $args | ForEach-Object { [Console]::Out.Write($_ + \"`n\") }");
            var shimPath = Path.Combine(dir.FullName, "claude.cmd");
            File.WriteAllText(shimPath, $"@powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\" %*");
            var (fileName, args, raw) = new ClaudeExecutable(shimPath, IsCmdShim: true)
                .BuildCommand(["a \"quoted\" <b> & c | d", "100% ^x!", ""]);

            var result = await new ProcessRunner().RunAsync(new ProcessRequest(fileName, args, raw, dir.FullName, null, []), CancellationToken.None);

            Assert.Equal("a \"quoted\" <b> & c | d\n100% ^x!\n\n", result.StandardOutput.Replace("\r", ""));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Find_prefers_exe_over_cmd_in_path()
    {
        var first = Directory.CreateTempSubdirectory("claudehotkey-a");
        var second = Directory.CreateTempSubdirectory("claudehotkey-b");
        try
        {
            File.WriteAllText(Path.Combine(first.FullName, "claude.cmd"), "");
            File.WriteAllText(Path.Combine(second.FullName, "claude.exe"), "");

            var found = ClaudeExecutable.Find(null, $"{first.FullName};{second.FullName}");

            Assert.NotNull(found);
            Assert.False(found.IsCmdShim);
            Assert.Equal(Path.Combine(second.FullName, "claude.exe"), found.Path);
        }
        finally
        {
            first.Delete(true);
            second.Delete(true);
        }
    }

    [Fact]
    public void Find_uses_cmd_shim_when_no_exe()
    {
        var dir = Directory.CreateTempSubdirectory("claudehotkey-c");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "claude.cmd"), "");

            var found = ClaudeExecutable.Find(null, dir.FullName);

            Assert.NotNull(found);
            Assert.True(found.IsCmdShim);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Find_with_missing_configured_path_returns_null()
    {
        Assert.Null(ClaudeExecutable.Find(@"C:\does\not\exist\claude.exe", ""));
    }

    [Fact]
    public async Task Preflight_reports_subscription()
    {
        var (cli, runner) = Create((request, _) => request.Arguments[0] == "auth"
            ? Ok("""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty"}""")
            : Ok("""
                {"type":"system","subtype":"init","apiKeySource":"none","model":"haiku"}
                {"type":"assistant","message":{}}
                {"type":"result","is_error":false,"result":"OK"}
                """));

        var result = await cli.PreflightAsync("haiku");

        Assert.True(result.Success);
        Assert.True(result.UsesSubscription);
        Assert.Equal("Abo aktiv", result.Message);
        Assert.Equal("claude.ai", result.AuthMethod);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("stream-json", runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task Preflight_warns_when_api_key_is_used()
    {
        var (cli, _) = Create((request, _) => request.Arguments[0] == "auth"
            ? Ok("""{"loggedIn":true,"authMethod":"api_key"}""")
            : Ok("""
                {"type":"system","subtype":"init","apiKeySource":"apiKeyHelper"}
                {"type":"result","is_error":false,"result":"OK"}
                """));

        var result = await cli.PreflightAsync("haiku");

        Assert.True(result.Success);
        Assert.False(result.UsesSubscription);
        Assert.Contains("API-Key", result.Message);
    }

    [Fact]
    public async Task Preflight_stops_early_when_not_logged_in()
    {
        var (cli, runner) = Create((_, _) => Ok("""{"loggedIn":false,"authMethod":"none"}""", exitCode: 1));

        var result = await cli.PreflightAsync("haiku");

        Assert.Equal(ClaudeErrorKind.NotLoggedIn, result.Error);
        Assert.Single(runner.Requests);
    }

    private sealed class FakeRunner(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> handler) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return handler(request, cancellationToken);
        }
    }
}
