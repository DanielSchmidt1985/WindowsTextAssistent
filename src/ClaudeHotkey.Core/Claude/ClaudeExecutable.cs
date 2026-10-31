using System.Text;

namespace ClaudeHotkey.Core.Claude;

/// <summary>
/// How to start the Claude CLI: either <c>claude.exe</c> directly, or an npm shim
/// (<c>claude.cmd</c>) through <c>cmd.exe</c>.
/// </summary>
public sealed record ClaudeExecutable(string Path, bool IsCmdShim)
{
    /// <summary>
    /// Looks for the CLI. A configured path wins; otherwise <c>claude.exe</c> and then
    /// <c>claude.cmd</c> are searched in <c>PATH</c>. Returns null when nothing is found.
    /// </summary>
    public static ClaudeExecutable? Find(string? configuredPath, string? pathVariable = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var path = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
            return File.Exists(path) ? FromPath(path) : null;
        }

        var directories = (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var fileName in new[] { "claude.exe", "claude.cmd" })
        {
            foreach (var directory in directories)
            {
                string candidate;
                try { candidate = System.IO.Path.Combine(directory.Trim('"'), fileName); }
                catch (ArgumentException) { continue; }
                if (File.Exists(candidate))
                    return FromPath(candidate);
            }
        }
        return null;
    }

    private static ClaudeExecutable FromPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        var isShim = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                  || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        return new ClaudeExecutable(path, isShim);
    }

    /// <summary>
    /// Builds what <see cref="ProcessRequest"/> needs: either an argument list (claude.exe) or,
    /// for a cmd shim, a ready-made raw command line for cmd.exe.
    /// </summary>
    public (string FileName, IReadOnlyList<string> Arguments, string? RawArguments) BuildCommand(IReadOnlyList<string> arguments)
    {
        if (!IsCmdShim)
            return (Path, arguments, null);

        // cmd.exe has its own parsing rules, so .NET's ArgumentList quoting is not enough there.
        // Same approach as the cross-spawn npm package: quote for the C runtime, then escape every
        // cmd metacharacter with ^ (twice, because the .cmd shim re-parses its arguments).
        var commandLine = new StringBuilder();
        commandLine.Append(EscapeForCmd(QuoteForCrt(Path), doubleEscape: false));
        foreach (var argument in arguments)
            commandLine.Append(' ').Append(EscapeForCmd(QuoteForCrt(argument), doubleEscape: true));

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        return (string.IsNullOrEmpty(comSpec) ? "cmd.exe" : comSpec, [], $"/d /s /c \"{commandLine}\"");
    }

    internal static string QuoteForCrt(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
                result.Append('\\', backslashes * 2 + 1);
            else
                result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(c);
        }
        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    internal static string EscapeForCmd(string value, bool doubleEscape)
    {
        const string metaCharacters = "()%!^\"<>&|";
        var once = new StringBuilder();
        foreach (var c in value)
        {
            if (metaCharacters.Contains(c))
                once.Append('^');
            once.Append(c);
        }
        return doubleEscape ? EscapeForCmd(once.ToString(), doubleEscape: false) : once.ToString();
    }
}
