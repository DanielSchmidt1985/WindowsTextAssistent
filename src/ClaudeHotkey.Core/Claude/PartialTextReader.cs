using System.Text;
using System.Text.Json;

namespace ClaudeHotkey.Core.Claude;

/// <summary>
/// Builds the answer text while it streams in, from the lines of
/// <c>--output-format stream-json --include-partial-messages</c>. Plain answers arrive as
/// <c>text_delta</c>; with <c>--json-schema</c> the answer is the input of the StructuredOutput
/// tool and arrives as <c>input_json_delta</c>, from which the "text" field is read while the
/// JSON is still incomplete. Only for display: the final result comes from the result line.
/// </summary>
public sealed class PartialTextReader
{
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _json = new();
    private bool _structured;
    private string _current = "";

    /// <summary>Feeds one stdout line. Returns the text so far when it changed, otherwise null.</summary>
    public string? Feed(string line)
    {
        if (!line.Contains("\"stream_event\"", StringComparison.Ordinal))
            return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("event", out var streamEvent) || streamEvent.ValueKind != JsonValueKind.Object)
                return null;
            Apply(streamEvent);
        }
        catch (JsonException)
        {
            return null;
        }

        var text = _structured ? ExtractTextField(_json.ToString()) ?? "" : _text.ToString();
        text = StripTextTag(text);
        if (text == _current)
            return null;
        _current = text;
        return text;
    }

    private void Apply(JsonElement streamEvent)
    {
        switch (GetString(streamEvent, "type"))
        {
            case "content_block_start":
                // A repeated StructuredOutput call (e.g. after a schema error) starts over.
                if (streamEvent.TryGetProperty("content_block", out var block) && GetString(block, "type") == "tool_use")
                {
                    _structured = true;
                    _json.Clear();
                }
                break;
            case "content_block_delta" when streamEvent.TryGetProperty("delta", out var delta):
                switch (GetString(delta, "type"))
                {
                    case "text_delta":
                        _text.Append(GetString(delta, "text"));
                        break;
                    case "input_json_delta":
                        _json.Append(GetString(delta, "partial_json"));
                        break;
                }
                break;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The model sometimes echoes the &lt;text&gt; wrapper of the input; hide it while streaming.</summary>
    private static string StripTextTag(string text)
    {
        var trimmed = text.TrimStart();
        if ("<text>".StartsWith(trimmed, StringComparison.Ordinal))
            return "";
        return trimmed.StartsWith("<text>", StringComparison.Ordinal) ? trimmed["<text>".Length..].TrimStart() : text.TrimStart();
    }

    /// <summary>
    /// Value of the top-level "text" field of a possibly incomplete JSON object, as far as it has
    /// arrived. Null when the field has not started yet.
    /// </summary>
    internal static string? ExtractTextField(string json)
    {
        var position = 0;
        SkipWhitespace(json, ref position);
        if (position >= json.Length || json[position] != '{')
            return null;
        position++;
        while (true)
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '"')
                return null;
            var (key, keyComplete) = ReadString(json, ref position);
            if (!keyComplete)
                return null;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != ':')
                return null;
            position++;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '"')
                return null; // not started yet, or not a string (the schema only has strings)
            var (value, valueComplete) = ReadString(json, ref position);
            if (key == "text")
                return value;
            if (!valueComplete)
                return null;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != ',')
                return null;
            position++;
        }
    }

    private static void SkipWhitespace(string json, ref int position)
    {
        while (position < json.Length && char.IsWhiteSpace(json[position]))
            position++;
    }

    /// <summary>Reads a JSON string starting at its opening quote; an unfinished escape at the end is dropped.</summary>
    private static (string Value, bool Complete) ReadString(string json, ref int position)
    {
        var builder = new StringBuilder();
        position++; // opening quote
        while (position < json.Length)
        {
            var c = json[position];
            if (c == '"')
            {
                position++;
                return (builder.ToString(), true);
            }
            if (c != '\\')
            {
                builder.Append(c);
                position++;
                continue;
            }
            if (position + 1 >= json.Length)
                break;
            var escaped = json[position + 1];
            if (escaped == 'u')
            {
                if (position + 6 > json.Length
                    || !int.TryParse(json.AsSpan(position + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                    break;
                builder.Append((char)code);
                position += 6;
                continue;
            }
            builder.Append(escaped switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                'b' => '\b',
                'f' => '\f',
                _ => escaped, // \" \\ \/
            });
            position += 2;
        }
        position = json.Length;
        return (builder.ToString(), false);
    }
}
