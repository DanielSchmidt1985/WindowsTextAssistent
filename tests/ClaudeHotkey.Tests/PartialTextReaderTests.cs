using System.Text.Json;
using ClaudeHotkey.Core.Claude;

namespace ClaudeHotkey.Tests;

public class PartialTextReaderTests
{
    // Line shapes taken from real runs with --include-partial-messages (Claude Code 2.1.240), shortened.
    private static string Event(string json) => $$"""{"type":"stream_event","event":{{json}},"session_id":"s"}""";

    private static string TextDelta(string text) =>
        Event($$$"""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":{{{JsonSerializer.Serialize(text)}}}}}""");

    private static string JsonDelta(string partial) =>
        Event($$$"""{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":{{{JsonSerializer.Serialize(partial)}}}}}""");

    private static readonly string ToolStart =
        Event("""{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"t","name":"StructuredOutput","input":{}}}""");

    private static List<string> FeedAll(params string[] lines)
    {
        var reader = new PartialTextReader();
        return lines.Select(reader.Feed).OfType<string>().ToList();
    }

    [Fact]
    public void Text_deltas_are_accumulated()
    {
        Assert.Equal(["Das ist ", "Das ist ein Satz."], FeedAll(TextDelta("Das ist "), TextDelta("ein Satz.")));
    }

    [Fact]
    public void Other_lines_change_nothing()
    {
        var updates = FeedAll(
            """{"type":"system","subtype":"init","tools":[]}""",
            Event("""{"type":"message_start","message":{}}"""),
            Event("""{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hm"}}"""),
            "keine JSON-Zeile",
            TextDelta("A"),
            """{"type":"result","is_error":false,"result":"A"}""");

        Assert.Equal(["A"], updates);
    }

    [Fact]
    public void Structured_output_shows_the_text_field_while_it_grows()
    {
        var updates = FeedAll(
            ToolStart,
            JsonDelta(""),
            JsonDelta("{\"text\": \"I"),
            JsonDelta(" have an apple"),
            JsonDelta(".\", \"feedback\": \"Korrig"),
            JsonDelta("iert\"}"));

        Assert.Equal(["I", "I have an apple", "I have an apple."], updates);
    }

    [Fact]
    public void A_new_structured_output_call_starts_over()
    {
        var updates = FeedAll(ToolStart, JsonDelta("{\"text\":\"alt\"}"), ToolStart, JsonDelta("{\"text\":\"neu"));

        Assert.Equal(["alt", "", "neu"], updates);
    }

    [Fact]
    public void Echoed_text_tag_is_hidden()
    {
        Assert.Equal(["Hallo"], FeedAll(TextDelta("<te"), TextDelta("xt>\nHallo")));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("{", null)]
    [InlineData("{\"te", null)]
    [InlineData("{\"text\"", null)]
    [InlineData("{\"text\": ", null)]
    [InlineData("{\"text\": \"", "")]
    [InlineData("{\"text\": \"a\\nb", "a\nb")]
    [InlineData("{\"text\": \"a\\", "a")]
    [InlineData("{\"text\": \"\\\"zitiert\\\" \\u00e4\\u00fc", "\"zitiert\" äü")]
    [InlineData("{\"text\": \"\\u00", "")]
    [InlineData("{\"feedback\": \"text: \\\"x\\\"\", \"text\": \"richtig", "richtig")]
    [InlineData("{\"feedback\": \"noch nicht fertig", null)]
    public void Text_field_is_read_from_incomplete_json(string json, string? expected)
    {
        Assert.Equal(expected, PartialTextReader.ExtractTextField(json));
    }
}
