using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Localization;

/// <summary>
/// Texts of the XAML windows, used as <c>{x:Static l:Ui.Name}</c>. Read when a window is created,
/// so a changed language applies to windows opened afterwards.
/// </summary>
public static class Ui
{
    private static string T(string german, string english) => Lang.T(german, english);

    // Popup
    public static string DragWidth => T("Breite ziehen", "Drag to change the width");
    public static string CloseEsc => T("Schließen (Esc)", "Close (Esc)");
    public static string Settings => T("Einstellungen", "Settings");
    public static string Run => T("Ausführen", "Run");
    public static string InputTip => T("Anweisung an Claude, Enter führt aus", "Instruction for Claude, Enter runs it");
    public static string EscCancels => T("  Esc bricht ab", "  Esc cancels");
    public static string Retry => T("Erneut versuchen", "Try again");
    public static string BackToResult => T("Zurück zum Ergebnis", "Back to the result");
    public static string Send => T("Senden", "Send");
    public static string RefinePlaceholder => T("Weiter bearbeiten, z. B. „in Du-Form“", "Keep editing, e.g. \"more casual\"");
    public static string RefineTip => T(
        "Änderungswunsch für dieses Ergebnis, Enter schickt ihn. Die gespeicherte Aktion bleibt unverändert.",
        "Change request for this result, Enter sends it. The saved action stays unchanged.");
    public static string PreviousVersion => T("Vorherige Fassung", "Previous version");
    public static string NextVersion => T("Nächste Fassung", "Next version");
    public static string Copy => T("Kopieren", "Copy");
    public static string Replace => T("Ersetzen", "Replace");
    public static string ReplaceTip => T("Markierten Text im Quellfenster ersetzen (Enter)", "Replace the selected text in the source window (Enter)");

    // Settings: frame and actions tab
    public static string SettingsTitle => T("ClaudeHotkey – Einstellungen", "ClaudeHotkey – Settings");
    public static string Close => T("Schließen", "Close");
    public static string Save => T("Speichern", "Save");
    public static string SaveTip => T("Speichern (Strg+S)", "Save (Ctrl+S)");
    public static string TabActions => T("Aktionen", "Actions");
    public static string New => T("Neu", "New");
    public static string Duplicate => T("Duplizieren", "Duplicate");
    public static string Delete => T("Löschen", "Delete");
    public static string MoveUp => T("Nach oben", "Move up");
    public static string MoveDown => T("Nach unten", "Move down");
    public static string OrDrag => T("oder ziehen", "or drag");
    public static string HasErrors => T("Hat Fehler", "Has errors");
    public static string NoActionSelected => T("Keine Aktion ausgewählt. Mit „Neu“ eine anlegen.", "No action selected. Create one with \"New\".");
    public static string LabelField => T("Name (Button im Popup)", "Name (button in the popup)");
    public static string IdLabel => T("Kennung:", "Id:");
    public static string PromptField => T("Prompt (Anweisung an Claude)", "Prompt (instruction for Claude)");
    public static string PromptHint => T(
        "Der markierte Text wird getrennt übergeben. {input} wird durch die Eingabe im Popup ersetzt (mit „Eingabefeld im Popup“).",
        "The selected text is passed separately. {input} is replaced by what is typed in the popup (with \"Input field in the popup\").");
    public static string FeedbackField => T("Zusätzliches Feedback (optional)", "Additional feedback (optional)");
    public static string FeedbackHint => T(
        "Was Claude außer dem Ergebnis sagen soll, z. B. ein Coaching zu den Fehlern. Steht im Popup unter dem Ergebnis und wird nie eingefügt. Kostet einige Sekunden mehr.",
        "What Claude should say besides the result, e.g. coaching on the mistakes. Shown in the popup below the result and never pasted. Takes a few seconds longer.");
    public static string ModelField => T("Modell", "Model");
    public static string ModelHint => T("Leer = Standardmodell aus „Allgemein“", "Empty = default model from \"General\"");
    public static string ActionHotkeyField => T("Eigener Hotkey (optional)", "Own hotkey (optional)");
    public static string ActionHotkeyHint => T(
        "Anklicken und Kombination drücken, für Doppeldruck die Taste zweimal (z. B. Strg+X+X). Entf löscht. Führt die Aktion direkt aus.",
        "Click and press the combination, for a double press the key twice (e.g. Ctrl+X+X). Del clears it. Runs the action directly.");
    public static string ShowReplace => T("Ersetzen-Button anzeigen", "Show the \"Replace\" button");
    public static string AskInputCheck => T("Eingabefeld im Popup", "Input field in the popup");
    public static string EnabledCheck => T("Aktiv (im Popup anzeigen)", "Active (show in the popup)");
    public static string Test => T("Testen", "Test");
    public static string SampleText => T("Beispieltext", "Sample text");
    public static string TestInputField => T("Eingabe im Popup ({input})", "Input in the popup ({input})");
    public static string Cancel => T("Abbrechen", "Cancel");

    // Settings: general tab
    public static string TabGeneral => T("Allgemein", "General");
    public static string DoubleCopyField => T("Zeitfenster für Doppeldruck (ms)", "Time window for a double press (ms)");
    public static string DefaultModelField => T("Standardmodell", "Default model");
    public static string PasteDelayField => T("Wartezeit beim Einfügen (ms)", "Paste delay (ms)");
    public static string PasteDelayHint => T("Erhöhen, wenn ein Programm beim Ersetzen noch den alten Text einfügt.",
        "Increase it if a program still pastes the old text when replacing.");
    public static string MainHotkeyField => T("Haupt-Hotkey (öffnet das Popup)", "Main hotkey (opens the popup)");
    public static string MainHotkeyHint => T(
        "Standard Strg+C+C. Für Doppeldruck die Taste zweimal drücken. Bei Strg+C+C und Strg+X+X kopiert das Programm selbst, sonst kopiert das Tool und stellt die Zwischenablage wieder her.",
        "Default Ctrl+C+C. For a double press, press the key twice. With Ctrl+C+C and Ctrl+X+X the program copies itself, otherwise the tool copies and restores the clipboard.");
    public static string TimeoutField => T("Timeout (Sekunden)", "Timeout (seconds)");
    public static string ClaudePathField => T("Pfad zur Claude-CLI (optional)", "Path to the Claude CLI (optional)");
    public static string ClaudePathHint => T("Leer = claude.exe im PATH suchen", "Empty = search claude.exe in PATH");
    public static string SystemPromptField => T("System-Prompt (gilt für alle Aktionen)", "System prompt (applies to all actions)");
    public static string Appearance => T("Darstellung", "Appearance");
    public static string ThemeField => T("Farbschema", "Color scheme");
    public static string ThemeHint => T("„Wie Windows“ folgt dem App-Modus von Windows (hell/dunkel), auch beim Umschalten.",
        "\"Like Windows\" follows the Windows app mode (light/dark), also when it changes.");
    public static string LanguageField => T("Sprache", "Language");
    public static string LanguageHint => T("Gilt für Fenster, die danach geöffnet werden.", "Applies to windows opened afterwards.");
    public static string LikeWindows => T("Wie Windows", "Like Windows");
    public static string Dark => T("Dunkel", "Dark");
    public static string Light => T("Hell", "Light");
    public static string ScaleField => T("Skalierung von Schrift und Bedienelementen (%)", "Scale of text and controls (%)");
    public static string ScaleHint => T("75–200. Schneller: Strg+Mausrad oder Strg+Plus/Minus in Popup und Einstellungen, Strg+0 = 100 %.",
        "75–200. Quicker: Ctrl+mouse wheel or Ctrl+Plus/Minus in the popup and settings, Ctrl+0 = 100 %.");
    public static string PopupWidthField => T("Breite des Popups (px)", "Popup width (px)");
    public static string PopupWidthHint => T("320–1600. Oder im Popup am rechten Rand ziehen. Die Höhe wächst mit dem Inhalt.",
        "320–1600. Or drag the right edge of the popup. The height grows with the content.");
    public static string Autostart => T("Mit Windows starten", "Start with Windows");
    public static string AutostartHint => T(
        "Beim Speichern wird dieses Programm unter HKCU\\…\\Run eingetragen bzw. wieder entfernt. Am besten aus der veröffentlichten .exe einschalten, nicht aus einem Test-Build.",
        "On saving, this program is added to or removed from HKCU\\…\\Run. Best switched on from the published .exe, not from a test build.");
}
