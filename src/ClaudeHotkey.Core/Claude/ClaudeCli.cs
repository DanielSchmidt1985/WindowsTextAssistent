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
/// <param name="FeedbackInstruction">
/// Optional: what Claude should say besides the result (e.g. language coaching). Then the answer
/// is requested as structured output with separate "text" and "feedback" fields.
/// </param>
public sealed record ClaudeRequest(
    string ActionPrompt, string Text, string Model, string? SystemPrompt, TimeSpan Timeout, string? FeedbackInstruction = null);

/// <param name="Text">The result that may replace the selection.</param>
/// <param name="Message">Short German message for the UI; null on success.</param>
/// <param name="Details">Raw CLI output for the "Details" section; null on success.</param>
/// <param name="Feedback">Only for reading, never pasted; null when the action asks for none or Claude had nothing to say.</param>
public sealed record ClaudeResult(ClaudeErrorKind Error, string? Text, string? Message, string? Details, string? Feedback = null)
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

    /// <summary>
    /// Structured output for actions with feedback. The CLI validates the answer against it and
    /// returns the object in "structured_output" (checked with Claude Code 2.1.240).
    /// </summary>
    public const string FeedbackSchema =
        """{"type":"object","properties":{"text":{"type":"string"},"feedback":{"type":"string"}},"required":["text","feedback"],"additionalProperties":false}""";

    private static bool WantsFeedback(ClaudeRequest request) => !string.IsNullOrWhiteSpace(request.FeedbackInstruction);

    public static IReadOnlyList<string> BuildArguments(ClaudeRequest request)
    {
        var arguments = new List<string>
        {
            "-p",
            "--model", request.Model,
            // stream-json needs --verbose; partial messages let the popup show the text while it is written.
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--system-prompt", BuildSystemPrompt(request),
        };
        if (WantsFeedback(request))
            arguments.AddRange(["--json-schema", FeedbackSchema]);
        arguments.AddRange(
        [
            "--no-session-persistence",
            "--strict-mcp-config",
            // Last on purpose: --tools takes a variadic list and would swallow following arguments.
            "--tools", "",
        ]);
        return arguments;
    }

    public static string BuildSystemPrompt(ClaudeRequest request)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            builder.Append(request.SystemPrompt.Trim()).Append("\n\n");
        builder.Append("Aufgabe:\n").Append(request.ActionPrompt.Trim());
        if (WantsFeedback(request))
        {
            // Spelled out because the general system prompt forbids explanations: without this
            // the model either drops the feedback or mixes it into the result.
            builder.Append("\n\nAntwortformat: ein Objekt mit zwei Feldern.\n")
                .Append("„text“: nur das Ergebnis der Aufgabe, genau so, wie es den markierten Text ersetzen soll, ohne Erklärung.\n")
                .Append("„feedback“: ").Append(request.FeedbackInstruction!.Trim()).Append('\n')
                .Append("Erklärungen und Hinweise gehören ausschließlich in „feedback“. ")
                .Append("Nur wenn es nach dieser Anweisung wirklich nichts zu sagen gibt, ist „feedback“ ein leerer String.");
        }
        return builder.ToString();
    }

    public static string BuildStandardInput(string text) => $"<text>\n{text}\n</text>";

    public Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(request, null, cancellationToken);

    /// <param name="onPartialText">
    /// Gets the answer text so far whenever it grows (on a worker thread). Only for display: the
    /// returned result is what counts.
    /// </param>
    public async Task<ClaudeResult> RunAsync(ClaudeRequest request, Action<string>? onPartialText, CancellationToken cancellationToken = default)
    {
        Action<string>? onLine = null;
        if (onPartialText is not null)
        {
            var reader = new PartialTextReader();
            onLine = line => { if (reader.Feed(line) is { } text) onPartialText(text); };
        }

        var outcome = await RunCliAsync(BuildArguments(request), BuildStandardInput(request.Text), request.Timeout, cancellationToken, onLine)
            .ConfigureAwait(false);
        if (outcome.Error is { } error)
            return new ClaudeResult(error.Kind, null, error.Message, error.Details);

        // Error details without the many partial-message lines, which only repeat the answer.
        var output = outcome.Output! with { StandardOutput = WithoutStreamEvents(outcome.Output!.StandardOutput) };
        var (_, parsed) = ParseStreamJson(output.StandardOutput);
        if (parsed is null)
        {
            var details = CombineOutput(output);
            return Fail(Classify(details, null) ?? ClaudeErrorKind.Failed, details);
        }

        if (parsed.IsError || output.ExitCode != 0)
            return Fail(Classify(parsed.Result, parsed.ApiErrorStatus) ?? ClaudeErrorKind.Failed, CombineOutput(output));

        var (answer, feedback) = WantsFeedback(request) ? SplitFeedback(parsed) : (parsed.Result, null);
        var text = CleanResponse(answer);
        return text.Length == 0
            ? Fail(ClaudeErrorKind.EmptyResponse, CombineOutput(output))
            : new ClaudeResult(ClaudeErrorKind.None, text, null, null, feedback);
    }

    internal static string WithoutStreamEvents(string stdout) =>
        string.Join('\n', stdout.Split('\n').Where(line => !line.StartsWith("{\"type\":\"stream_event\"", StringComparison.Ordinal)));

    /// <summary>
    /// Text and feedback from a structured answer: "structured_output" first, then "result" as
    /// JSON. If neither holds the object, the whole answer counts as text (as without feedback).
    /// </summary>
    internal static (string Text, string? Feedback) SplitFeedback(ParsedResult parsed)
    {
        if (parsed.StructuredText is { } structured)
            return (structured, NullIfEmpty(parsed.StructuredFeedback));
        try
        {
            using var document = JsonDocument.Parse(parsed.Result.Trim());
            if (ReadTextAndFeedback(document.RootElement) is { } fromResult)
                return (fromResult.Text, NullIfEmpty(fromResult.Feedback));
        }
        catch (JsonException)
        {
        }
        return (parsed.Result, null);
    }

    private static (string Text, string? Feedback)? ReadTextAndFeedback(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            return null;
        var feedback = element.TryGetProperty("feedback", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        return (text.GetString()!, feedback);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
        IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken cancellationToken,
        Action<string>? onLine = null)
    {
        var executable = _locate();
        if (executable is null)
            return new CliOutcome(null, new CliError(ClaudeErrorKind.CliNotFound, MessageFor(ClaudeErrorKind.CliNotFound), null));

        Directory.CreateDirectory(_workingDirectory);
        var (fileName, args, rawArgs) = executable.BuildCommand(arguments);
        var request = new ProcessRequest(fileName, args, rawArgs, _workingDirectory, standardInput, RemovedEnvironmentVariables, onLine);

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

    /// <param name="StructuredText">"text" of "structured_output", only with --json-schema.</param>
    internal sealed record ParsedResult(bool IsError, string Result, int? ApiErrorStatus, string? StructuredText = null, string? StructuredFeedback = null);

    /// <summary>Reads the result line of stream-json output; the answer is in <c>result</c>.</summary>
    private static bool TryReadResult(JsonElement root, out ParsedResult parsed)
    {
        parsed = new ParsedResult(true, "", null);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result))
            return false;

        var isError = root.TryGetProperty("is_error", out var isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True;
        int? status = root.TryGetProperty("api_error_status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32()
            : null;
        var structured = root.TryGetProperty("structured_output", out var s) ? ReadTextAndFeedback(s) : null;
        parsed = new ParsedResult(isError, result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString(), status,
            structured?.Text, structured?.Feedback);
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
