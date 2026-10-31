using System.Text;
using System.Text.Json;

namespace ClaudeHotkey.Core.Config;

/// <summary>
/// Look and window geometry, kept in ui.json next to actions.json. Separate on purpose: dragging
/// the popup wider writes this file, which must not race with the settings dialog saving
/// actions.json.
/// </summary>
public sealed class UiState
{
    public const int DefaultPopupWidth = 464;
    public const int MinPopupWidth = 320;
    public const int MaxPopupWidth = 1600;
    public const int MinScale = 75;
    public const int MaxScale = 200;
    public const int ScaleStep = 10;

    /// <summary>On-screen width of the popup in DIPs.</summary>
    public int PopupWidth { get; set; } = DefaultPopupWidth;

    /// <summary>Scale of text and controls in percent, for all windows.</summary>
    public int Scale { get; set; } = 100;

    /// <summary>Last bounds of the settings window; null until it was moved or resized once.</summary>
    public WindowBounds? SettingsWindow { get; set; }

    public double ScaleFactor => Scale / 100.0;

    /// <summary>Brings every value into its allowed range (the file can be edited by hand).</summary>
    public UiState Normalized()
    {
        PopupWidth = Math.Clamp(PopupWidth, MinPopupWidth, MaxPopupWidth);
        Scale = Math.Clamp(Scale, MinScale, MaxScale);
        if (SettingsWindow is { Width: < 200 } or { Height: < 200 })
            SettingsWindow = null;
        return this;
    }

    /// <summary>Next scale for Ctrl+Plus (+1), Ctrl+Minus (-1) or Ctrl+0 (0 = back to 100 %).</summary>
    public static int StepScale(int current, int direction) => direction == 0
        ? 100
        : Math.Clamp((int)Math.Round((current + direction * ScaleStep) / (double)ScaleStep) * ScaleStep, MinScale, MaxScale);
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);

/// <summary>Reads and writes ui.json. Broken or missing files fall back to the defaults.</summary>
public sealed class UiStateStore(string directory)
{
    public const string FileName = "ui.json";

    public string FilePath { get; } = Path.Combine(directory, FileName);

    public UiState Current { get; private set; } = new();

    public UiState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = (JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath), ActionConfig.JsonOptions) ?? new UiState()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Current = new UiState(); // the look is not worth a warning; defaults are fine
        }
        return Current;
    }

    /// <summary>Applies <paramref name="change"/> to the current state and writes it atomically.</summary>
    public UiState Update(Action<UiState> change)
    {
        change(Current);
        Current.Normalized();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, ActionConfig.JsonOptions), new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved this time; the change still applies until the app exits.
        }
        return Current;
    }
}
