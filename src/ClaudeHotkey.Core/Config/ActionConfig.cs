using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeHotkey.Core.Hotkeys;

namespace ClaudeHotkey.Core.Config;

public sealed class AppSettings
{
    public int DoubleCopyMs { get; set; } = 400;
    public string SecondHotkey { get; set; } = "ctrl+alt+space";
    public string Model { get; set; } = "haiku";
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Wait after Ctrl+V before the old clipboard content is put back.</summary>
    public int PasteDelayMs { get; set; } = 500;
    public string SystemPrompt { get; set; } = "";
    public bool Autostart { get; set; }

    /// <summary>Empty: search claude.exe / claude.cmd in PATH.</summary>
    public string ClaudePath { get; set; } = "";
}

public sealed class ActionDefinition
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Prompt { get; set; } = "";

    /// <summary>True shows the "Ersetzen" button, false only "Kopieren".</summary>
    public bool Replace { get; set; } = true;

    /// <summary>Shows an input field in the popup; its text replaces {input} in the prompt.</summary>
    public bool AskInput { get; set; }

    /// <summary>Overrides <see cref="AppSettings.Model"/> for this action.</summary>
    public string? Model { get; set; }

    /// <summary>Runs the action directly on the selected text.</summary>
    public string? Hotkey { get; set; }

    public bool Enabled { get; set; } = true;

    public string EffectiveModel(AppSettings settings) => string.IsNullOrWhiteSpace(Model) ? settings.Model : Model.Trim();

    /// <summary>The prompt with {input} replaced by what the user typed in the popup.</summary>
    public string BuildPrompt(string? input) => Prompt.Replace("{input}", input?.Trim() ?? "", StringComparison.Ordinal);
}

public sealed class ActionConfig
{
    public AppSettings Settings { get; set; } = new();
    public List<ActionDefinition> Actions { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<ActionDefinition> EnabledActions => Actions.Where(a => a.Enabled);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep umlauts and <text> readable in the file instead of ü escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses and validates. Throws <see cref="ActionConfigException"/> with German messages.</summary>
    public static ActionConfig Parse(string json)
    {
        ActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ActionConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (Zeile {line + 1})" : "";
            throw new ActionConfigException([$"actions.json ist kein gültiges JSON{where}: {ex.Message}"]);
        }
        if (config is null)
            throw new ActionConfigException(["actions.json ist leer."]);

        config.Settings ??= new AppSettings();
        config.Actions ??= [];
        var errors = config.Validate();
        if (errors.Count > 0)
            throw new ActionConfigException(errors);
        return config;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Settings.DoubleCopyMs is < 100 or > 2000)
            errors.Add("doubleCopyMs muss zwischen 100 und 2000 liegen.");
        if (Settings.TimeoutSeconds is < 5 or > 600)
            errors.Add("timeoutSeconds muss zwischen 5 und 600 liegen.");
        if (Settings.PasteDelayMs is < 100 or > 5000)
            errors.Add("pasteDelayMs muss zwischen 100 und 5000 liegen.");
        if (string.IsNullOrWhiteSpace(Settings.Model))
            errors.Add("model darf nicht leer sein.");
        if (!HotkeyGesture.TryParse(Settings.SecondHotkey, out var secondHotkey))
            errors.Add($"secondHotkey „{Settings.SecondHotkey}“ ist keine gültige Tastenkombination.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hotkeys = new Dictionary<HotkeyGesture, string>();
        if (secondHotkey is not null)
            hotkeys[secondHotkey] = "secondHotkey";

        for (var i = 0; i < Actions.Count; i++)
        {
            var action = Actions[i];
            var name = string.IsNullOrWhiteSpace(action.Label) ? $"Aktion {i + 1}" : $"„{action.Label}“";

            if (string.IsNullOrWhiteSpace(action.Id))
                errors.Add($"{name}: id fehlt.");
            else if (!ids.Add(action.Id))
                errors.Add($"{name}: id „{action.Id}“ ist doppelt vergeben.");
            if (string.IsNullOrWhiteSpace(action.Label))
                errors.Add($"{name}: label fehlt.");
            if (string.IsNullOrWhiteSpace(action.Prompt))
                errors.Add($"{name}: prompt ist leer.");
            if (action.AskInput && !action.Prompt.Contains("{input}", StringComparison.Ordinal))
                errors.Add($"{name}: askInput ist an, aber der Prompt enthält kein {{input}}.");

            if (!string.IsNullOrWhiteSpace(action.Hotkey))
            {
                if (!HotkeyGesture.TryParse(action.Hotkey, out var gesture))
                    errors.Add($"{name}: hotkey „{action.Hotkey}“ ist keine gültige Tastenkombination.");
                else if (hotkeys.TryGetValue(gesture, out var owner))
                    errors.Add($"{name}: hotkey {gesture} ist schon für {owner} vergeben.");
                else
                    hotkeys[gesture] = name;
            }
        }
        return errors;
    }
}

public sealed class ActionConfigException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Owns actions.json in %APPDATA%\ClaudeHotkey. Keeps the last valid configuration when the file
/// is broken, and writes atomically (temporary file, then rename).
/// </summary>
public sealed class ActionConfigStore
{
    public const string FileName = "actions.json";

    private readonly string _path;
    private readonly Func<string> _defaultJson;

    public ActionConfigStore(string directory, Func<string> defaultJson)
    {
        Directory = directory;
        _path = Path.Combine(directory, FileName);
        _defaultJson = defaultJson;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeHotkey");

    public string Directory { get; }

    public string FilePath => _path;

    /// <summary>The configuration in effect: the last one that loaded without errors.</summary>
    public ActionConfig Current { get; private set; } = new();

    /// <summary>Errors of the last load attempt; empty when the file was fine.</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    private DateTime _lastWriteTime;

    /// <summary>
    /// Loads the file, creating it from the defaults on first start. Cheap when the file has not
    /// changed. Returns true when <see cref="Current"/> was (re)loaded or is unchanged and valid.
    /// </summary>
    public bool Reload()
    {
        try
        {
            if (!File.Exists(_path))
            {
                System.IO.Directory.CreateDirectory(Directory);
                WriteAtomically(_defaultJson());
            }

            var writeTime = File.GetLastWriteTimeUtc(_path);
            if (writeTime == _lastWriteTime)
                return LoadErrors.Count == 0;

            var config = ActionConfig.Parse(File.ReadAllText(_path));
            Current = config;
            LoadErrors = [];
            _lastWriteTime = writeTime;
            return true;
        }
        catch (ActionConfigException ex)
        {
            LoadErrors = ex.Errors;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadErrors = [$"actions.json konnte nicht gelesen werden: {ex.Message}"];
        }
        // Remember the broken version so it is not parsed again on every call.
        try { _lastWriteTime = File.GetLastWriteTimeUtc(_path); } catch (IOException) { }
        return false;
    }

    /// <summary>Validates and saves. Throws <see cref="ActionConfigException"/> without touching the file.</summary>
    public void Save(ActionConfig config)
    {
        var errors = config.Validate();
        if (errors.Count > 0)
            throw new ActionConfigException(errors);

        System.IO.Directory.CreateDirectory(Directory);
        WriteAtomically(config.ToJson());
        Current = ActionConfig.Parse(File.ReadAllText(_path));
        LoadErrors = [];
        _lastWriteTime = File.GetLastWriteTimeUtc(_path);
    }

    private void WriteAtomically(string json)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, _path, overwrite: true);
    }
}
