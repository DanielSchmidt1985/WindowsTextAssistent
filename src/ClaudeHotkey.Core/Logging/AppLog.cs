using System.Globalization;
using System.Text;

namespace ClaudeHotkey.Core.Logging;

/// <summary>
/// Small log file in %APPDATA%\ClaudeHotkey. By design it only takes an event name, an action id,
/// a duration and an error code: there is no parameter for free text, so selected text or Claude's
/// answer cannot end up in it. Rotates to log.1.txt at <paramref name="maxBytes"/>.
/// </summary>
public sealed class AppLog(string path, long maxBytes = 1_000_000)
{
    public const string FileName = "log.txt";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _lock = new();

    public string FilePath => path;

    /// <param name="eventName">Fixed word such as "start", "capture", "claude", "replace".</param>
    /// <param name="action">Action id from actions.json (never its label or prompt).</param>
    /// <param name="code">Error kind or outcome, e.g. "None", "Timeout", "NoText".</param>
    public void Write(string eventName, string? action = null, TimeSpan? duration = null, string? code = null)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append("  ").Append(Token(eventName));
        if (action is not null)
            line.Append("  action=").Append(Token(action));
        if (duration is { } d)
            line.Append("  ms=").Append(((long)d.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        if (code is not null)
            line.Append("  code=").Append(Token(code));
        line.Append(Environment.NewLine);

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                    File.Move(path, RotatedPath, overwrite: true);
                File.AppendAllText(path, line.ToString(), Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the tool.
            }
        }
    }

    public string RotatedPath => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".1" + Path.GetExtension(path));

    /// <summary>Keeps entries on one line and short: ids and codes only.</summary>
    private static string Token(string value)
    {
        var clean = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '+').ToArray());
        return clean.Length <= 40 ? clean : clean[..40];
    }
}
