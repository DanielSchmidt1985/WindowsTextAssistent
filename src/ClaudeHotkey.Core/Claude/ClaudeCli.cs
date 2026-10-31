using System.Text;
using System.Text.Json;

namespace ClaudeHotkey.Core.Claude;

public enum ClaudeErrorKind
{
    None,
    CliNotFound,
    NotLoggedIn,
    LimitReached,
    EmptyResponse,
    Timeout,
    Cancelled,
    Failed,
}

/// <param name="ActionPrompt">Instruction of the chosen action, e.g. "Korrigiere ...".</param>
/// <param name="Text">The selected text. Sent over stdin, never as an argument.</param>
/// <param name="SystemPrompt">General rules from the settings; the action prompt is appended to it.</param>
public sealed record ClaudeRequest(string ActionPrompt, string Text, string Model, string? SystemPrompt, TimeSpan Timeout);

/// <param name="Message">Short German message for the UI; null on success.</param>
/// <param name="Details">Raw CLI output for the "Details" section; null on success.</param>
public sealed record ClaudeResult(ClaudeErrorKind Error, string? Text, string? Message, string? Details)
{
    public bool Success => Error == ClaudeErrorKind.None;
}

/// <param name="UsesSubscription">False when the CLI reported an API key as its credential source.</param>
public sealed record PreflightResult(
    ClaudeErrorKind Error,
    bool UsesSubscription,
    string? AuthMethod,
    string? ApiKeySource,
    string Message,
    string? Details)
{
    public bool Success => Error == ClaudeErrorKind.None;
}

/// <summary>
/// Runs <c>claude -p</c> for one action. Every call starts in an empty working directory, without
/// tools, MCP servers or session persistence, and with API-key variables removed from the
/// environment so the subscription login is used.
/// </summary>
public sealed class ClaudeCli
{
    /// <summary>
    /// Removed from the child environment. API key and token would make Claude Code bill the API
    /// instead of the subscription. The rest is inherited when this tool is started from inside a
    /// Claude Code session: CLAUDE_CODE_SIMPLE switches on --bare (never reads the subscription
    /// login), ANTHROPIC_BASE_URL points at the parent session's proxy.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedEnvironmentVariables =
        ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDECODE", "CLAUDE_CODE_*", "CLAUDE_AGENT_SDK_*"];

    private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(60);

    private readonly IProcessRunner _runner;
    private readonly Func<ClaudeExecutable?> _locate;
    private readonly string _workingDirectory;

    public ClaudeCli(string? configuredPath = null)
        : this(new ProcessRunner(), () => ClaudeExecutable.Find(configuredPath), DefaultWorkingDirectory())
    {
    }

    public ClaudeCli(IProcessRunner runner, Func<ClaudeExecutable?> locate, string workingDirectory)
    {
        _runner = runner;
        _locate = locate;
        _workingDirectory = workingDirectory;
    }

    public static string DefaultWorkingDirectory() => Path.Combine(Path.GetTempPath(), "ClaudeHotkey", "work");

    public static IReadOnlyList<string> BuildArguments(ClaudeRequest request) =>
    [
        "-p",
        "--model", request.Model,
        "--output-format", "json",
        "--system-prompt", BuildSystemPrompt(request),
        "--no-session-persistence",
        "--strict-mcp-config",
        // Last on purpose: --tools takes a variadic list and would swallow following arguments.
        "--tools", "",
    ];

    public static string BuildSystemPrompt(ClaudeRequest request)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            builder.Append(request.SystemPrompt.Trim()).Append("\n\n");
        builder.Append("Aufgabe:\n").Append(request.ActionPrompt.Trim());
        return builder.ToString();
    }

    public static string BuildStandardInput(string text) => $"<text>\n{text}\n</text>";

    public async Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken = default)
    {
        var outcome = await RunCliAsync(BuildArguments(request), BuildStandardInput(request.Text), request.Timeout, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Error is { } error)
            return new ClaudeResult(error.Kind, null, error.Message, error.Details);

        var output = outcome.Output!;
        if (!TryParseResult(output.StandardOutput, out var parsed))
        {
            var details = CombineOutput(output);
            return Fail(Classify(details, null) ?? ClaudeErrorKind.Failed, details);
        }

        if (parsed.IsError || output.ExitCode != 0)
            return Fail(Classify(parsed.Result, parsed.ApiErrorStatus) ?? ClaudeErrorKind.Failed, CombineOutput(output));

        var text = CleanResponse(parsed.Result);
        return text.Length == 0
            ? Fail(ClaudeErrorKind.EmptyResponse, CombineOutput(output))
            : new ClaudeResult(ClaudeErrorKind.None, text, null, null);
    }

    /// <summary>
    /// Checks login and credential source: <c>claude auth status</c> first (free), then one short
    /// <c>-p</c> call whose init message names the API key source.
    /// </summary>
    public async Task<PreflightResult> PreflightAsync(string model, CancellationToken cancellationToken = default)
    {
        var status = await RunCliAsync(["auth", "status"], null, PreflightTimeout, cancellationToken).ConfigureAwait(false);
        if (status.Error is { } statusError)
            return new PreflightResult(statusError.Kind, false, null, null, statusError.Message, statusError.Details);

        var (loggedIn, authMethod) = ParseAuthStatus(status.Output!.StandardOutput);
        if (loggedIn == false)
            return PreflightFail(ClaudeErrorKind.NotLoggedIn, authMethod, null, CombineOutput(status.Output));

        string[] arguments =
        [
            "-p",
            "--model", model,
            "--output-format", "stream-json",
            "--verbose",
            "--no-session-persistence",
            "--strict-mcp-config",
            "--tools", "",
        ];
        var probe = await RunCliAsync(arguments, "Antworte nur mit OK.", PreflightTimeout, cancellationToken).ConfigureAwait(false);
        if (probe.Error is { } probeError)
            return new PreflightResult(probeError.Kind, false, authMethod, null, probeError.Message, probeError.Details);

        var (apiKeySource, result) = ParseStreamJson(probe.Output!.StandardOutput);
        if (result is null || result.IsError || probe.Output.ExitCode != 0)
        {
            var kind = Classify(result?.Result ?? CombineOutput(probe.Output), result?.ApiErrorStatus) ?? ClaudeErrorKind.Failed;
            return PreflightFail(kind, authMethod, apiKeySource, CombineOutput(probe.Output));
        }

        var usesSubscription = apiKeySource is null or "none";
        var message = usesSubscription
            ? "Abo aktiv"
            : $"Warnung: Claude nutzt einen API-Key ({apiKeySource}), nicht das Abo.";
        return new PreflightResult(ClaudeErrorKind.None, usesSubscription, authMethod, apiKeySource, message, null);
    }

    public static string MessageFor(ClaudeErrorKind kind, TimeSpan? timeout = null) => kind switch
    {
        ClaudeErrorKind.CliNotFound => "Claude-CLI nicht gefunden. Pfad in den Einstellungen prüfen oder Claude Code installieren.",
        ClaudeErrorKind.NotLoggedIn => "Claude-CLI ist nicht angemeldet. Im Terminal „claude auth login“ ausführen.",
        ClaudeErrorKind.LimitReached => "Nutzungslimit des Abos erreicht. Später erneut versuchen.",
        ClaudeErrorKind.EmptyResponse => "Claude hat keine Antwort geliefert.",
        ClaudeErrorKind.Timeout => timeout is { } t
            ? $"Keine Antwort nach {t.TotalSeconds:0} Sekunden, Aufruf abgebrochen."
            : "Zeitüberschreitung, Aufruf abgebrochen.",
        ClaudeErrorKind.Cancelled => "Abgebrochen.",
        ClaudeErrorKind.Failed => "Claude-Aufruf fehlgeschlagen.",
        _ => "",
    };

    private sealed record CliError(ClaudeErrorKind Kind, string Message, string? Details);

    private sealed record CliOutcome(ProcessResult? Output, CliError? Error);

    private async Task<CliOutcome> RunCliAsync(
        IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var executable = _locate();
        if (executable is null)
            return new CliOutcome(null, new CliError(ClaudeErrorKind.CliNotFound, MessageFor(ClaudeErrorKind.CliNotFound), null));

        Directory.CreateDirectory(_workingDirectory);
        var (fileName, args, rawArgs) = executable.BuildCommand(arguments);
        var request = new ProcessRequest(fileName, args, rawArgs, _workingDirectory, standardInput, RemovedEnvironmentVariables);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var output = await _runner.RunAsync(request, timeoutSource.Token).ConfigureAwait(false);
            return new CliOutcome(output, null);
        }
        catch (ProcessStartException ex)
        {
            return new CliOutcome(null, new CliError(ClaudeErrorKind.CliNotFound, MessageFor(ClaudeErrorKind.CliNotFound), ex.Message));
        }
        catch (OperationCanceledException)
        {
            var kind = cancellationToken.IsCancellationRequested ? ClaudeErrorKind.Cancelled : ClaudeErrorKind.Timeout;
            return new CliOutcome(null, new CliError(kind, MessageFor(kind, timeout), null));
        }
    }

    private static ClaudeResult Fail(ClaudeErrorKind kind, string? details) => new(kind, null, MessageFor(kind), details);

    private static PreflightResult PreflightFail(ClaudeErrorKind kind, string? authMethod, string? apiKeySource, string? details) =>
        new(kind, false, authMethod, apiKeySource, MessageFor(kind), details);

    internal sealed record ParsedResult(bool IsError, string Result, int? ApiErrorStatus);

    /// <summary>Reads the single JSON object of <c>--output-format json</c>; the answer is in <c>result</c>.</summary>
    internal static bool TryParseResult(string stdout, out ParsedResult parsed)
    {
        parsed = new ParsedResult(true, "", null);
        try
        {
            using var document = JsonDocument.Parse(stdout.Trim());
            return TryReadResult(document.RootElement, out parsed);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadResult(JsonElement root, out ParsedResult parsed)
    {
        parsed = new ParsedResult(true, "", null);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result))
            return false;

        var isError = root.TryGetProperty("is_error", out var isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True;
        int? status = root.TryGetProperty("api_error_status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32()
            : null;
        parsed = new ParsedResult(isError, result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString(), status);
        return true;
    }

    internal static (bool? LoggedIn, string? AuthMethod) ParseAuthStatus(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout.Trim());
            var root = document.RootElement;
            bool? loggedIn = root.TryGetProperty("loggedIn", out var l) && l.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? l.GetBoolean()
                : null;
            var authMethod = root.TryGetProperty("authMethod", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            return (loggedIn, authMethod);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Reads <c>apiKeySource</c> from the init line and the final result line of stream-json output.</summary>
    internal static (string? ApiKeySource, ParsedResult? Result) ParseStreamJson(string stdout)
    {
        string? apiKeySource = null;
        ParsedResult? result = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type))
                    continue;
                if (type.GetString() == "system" && root.TryGetProperty("apiKeySource", out var source) && source.ValueKind == JsonValueKind.String)
                    apiKeySource = source.GetString();
                else if (type.GetString() == "result" && TryReadResult(root, out var parsed))
                    result = parsed;
            }
            catch (JsonException)
            {
                // Not every line has to be JSON (e.g. warnings); skip it.
            }
        }
        return (apiKeySource, result);
    }

    /// <summary>Maps CLI error text to an error kind. Null when nothing specific matches.</summary>
    internal static ClaudeErrorKind? Classify(string? text, int? apiErrorStatus)
    {
        if (apiErrorStatus is 401 or 403)
            return ClaudeErrorKind.NotLoggedIn;
        if (apiErrorStatus == 429)
            return ClaudeErrorKind.LimitReached;
        if (string.IsNullOrEmpty(text))
            return null;

        string[] loginHints = ["failed to authenticate", "not logged in", "/login", "oauth", "invalid api key", "please log in"];
        string[] limitHints = ["usage limit", "rate limit", "limit reached", "hit your limit", "limit will reset", "out of extra usage"];

        if (loginHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            return ClaudeErrorKind.NotLoggedIn;
        if (limitHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            return ClaudeErrorKind.LimitReached;
        return null;
    }

    /// <summary>Trims the answer and drops a &lt;text&gt; wrapper the model sometimes echoes back.</summary>
    internal static string CleanResponse(string result)
    {
        var text = result.Trim();
        if (text.StartsWith("<text>", StringComparison.Ordinal) && text.EndsWith("</text>", StringComparison.Ordinal))
            text = text["<text>".Length..^"</text>".Length].Trim();
        return text;
    }

    private static string CombineOutput(ProcessResult output)
    {
        var builder = new StringBuilder();
        builder.Append("Exit-Code: ").Append(output.ExitCode);
        if (!string.IsNullOrWhiteSpace(output.StandardOutput))
            builder.Append("\n\nstdout:\n").Append(output.StandardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(output.StandardError))
            builder.Append("\n\nstderr:\n").Append(output.StandardError.Trim());
        return builder.ToString();
    }
}
