using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Themes;

/// <summary>
/// Optional %APPDATA%\ClaudeHotkey\theme.json, e.g. <c>{ "AccentBrush": "#3794FF" }</c>. Each entry
/// replaces one *Brush token of the active palette (Colors.Dark/Light.xaml) at runtime, so the look
/// can be tuned without a rebuild. ThemeManager applies it again after every palette switch.
/// </summary>
public static class ThemeOverrides
{
    public const string FileName = "theme.json";

    /// <returns>Problems found in the file; empty when it is missing or fine.</returns>
    public static IReadOnlyList<string> Apply(ResourceDictionary resources, string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            return [];

        Dictionary<string, string>? tokens;
        try
        {
            tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path),
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [Lang.T($"theme.json konnte nicht gelesen werden: {ex.Message}", $"theme.json could not be read: {ex.Message}")];
        }

        var problems = new List<string>();
        foreach (var (key, value) in tokens ?? [])
        {
            // The indexer also searches merged dictionaries; Contains would not.
            if (!key.EndsWith("Brush", StringComparison.Ordinal) || resources[key] is not SolidColorBrush)
            {
                problems.Add(Lang.T($"theme.json: unbekanntes Token „{key}“.", $"theme.json: unknown token \"{key}\"."));
                continue;
            }
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
                brush.Freeze();
                resources[key] = brush;
            }
            catch (FormatException)
            {
                problems.Add(Lang.T($"theme.json: „{value}“ ist keine Farbe (Token {key}).", $"theme.json: \"{value}\" is not a colour (token {key})."));
            }
        }
        return problems;
    }
}
