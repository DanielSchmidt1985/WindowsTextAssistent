# ClaudeHotkey

Persönliches Windows-Tool: Strg+C+C übernimmt den markierten Text, ein Popup bietet Aktionen wie
Übersetzen oder Verbessern an, die Aktion läuft über die lokale Claude-CLI (`claude -p`) mit dem
vorhandenen Claude-Abo, und das Ergebnis lässt sich per Button ins Quellfenster einsetzen.

Stand: **Phase 3 (Popup, Aktionen und Design)**. Ersetzen (Phase 4), Einstellungsdialog (Phase 5) und Tray
(Phase 6) folgen. Bis zum Tray-Icon zeigt die App ein vorläufiges Statusfenster; Schließen beendet das Tool.

## Bedienung

Text markieren, **Strg+C+C** oder **Strg+Alt+Leertaste** drücken. Das Popup öffnet sich an der Maus.

- Aktion per Klick oder mit den Tasten **1–9** starten. „Freitext“ fragt zuerst nach einer Anweisung (Enter führt aus).
- Während Claude arbeitet, läuft ein Balken; **Esc** bricht ab (der CLI-Prozess wird beendet) und schließt.
- **Kopieren** legt das Ergebnis in die Zwischenablage und schließt das Popup. **Ersetzen** ist noch ausgegraut (Phase 4).
- Bei Fehlern steht eine kurze Meldung im Popup, die Rohausgabe der CLI unter „Details“, dazu „Erneut versuchen“.
- Ein neuer Auslöser ersetzt ein offenes Popup und bricht dessen Aufruf ab.

## Konfiguration

`%APPDATA%\ClaudeHotkey\actions.json` entsteht beim ersten Start aus `src/ClaudeHotkey.Core/Config/actions.default.json`
(eingebettet in die DLL). Die Datei wird vor jedem Popup neu gelesen, wenn sie sich geändert hat; Änderungen an
Aktionen und Prompts gelten also ohne Neustart. `doubleCopyMs` gilt ab der nächsten Übernahme, `secondHotkey` erst
nach einem Neustart (live ab Phase 5).

- Erlaubt sind Kommentare (`//`) und Kommas am Ende.
- Geprüft wird: eindeutige `id`, nicht leerer `prompt`, `askInput` nur mit `{input}` im Prompt, gültige und nicht
  doppelt vergebene Hotkeys, Wertebereiche für `doubleCopyMs` (100–2000) und `timeoutSeconds` (5–600).
- Ist die Datei fehlerhaft, bleibt die zuletzt gültige Konfiguration aktiv; die Fehler stehen im Statusfenster.
- Zusätzlich zum Plan: `settings.autostart`, `settings.claudePath` und pro Aktion `hotkey` und `enabled`
  (für den Einstellungsdialog). `enabled: false` blendet eine Aktion im Popup aus.

### Design anpassen

Alle Farben stehen als Token in `src/ClaudeHotkey/Themes/Theme.Neptun.xaml`. Ohne Neubau lassen sie sich in
`%APPDATA%\ClaudeHotkey\theme.json` überschreiben, z. B. `{ "AccentBrush": "#3794FF" }`. Neben den Tokens aus dem
Plan gibt es `HoverBrush`, `PressedBrush` und `SelectionBrush`.

Zum Vergleichen mit dem Referenz-Screenshot öffnet `ClaudeHotkey.exe --demo <zustand>` das Popup mit Beispieltext,
ohne Hook und ohne Claude-Aufruf. Zustände: `choose`, `input`, `running`, `result`, `error`, `nocapture`.

## Voraussetzungen

- Windows 10/11, .NET 8 SDK zum Bauen
- Claude Code CLI, angemeldet mit dem Abo: in einem normalen Terminal `claude auth login` ausführen.
  Prüfen mit `claude auth status` (`"loggedIn": true`). Der Login der Claude-Desktop-App zählt dafür nicht;
  die CLI speichert ihren eigenen in `%USERPROFILE%\.claude\.credentials.json`.

## Projektstruktur

| Pfad | Inhalt |
| --- | --- |
| `src/ClaudeHotkey` | WPF-App (in Phase 1 nur Preflight beim Start) |
| `src/ClaudeHotkey.Core` | Logik ohne Oberfläche, u. a. `Claude/ClaudeCli.cs` |
| `tests/ClaudeHotkey.Tests` | xUnit-Tests |
| `tools/ClaudeHotkey.CliCheck` | Konsolen-Testroutine für den CLI-Kern |

## Bauen und prüfen

```
dotnet build ClaudeHotkey.sln
dotnet test
dotnet run --project tools/ClaudeHotkey.CliCheck -- "Ein Text mit Fehlern"
```

Optionen der Testroutine: `--model <alias>`, `--claude <pfad zur claude.exe>`.

## CLI-Aufruf

Geprüft mit Claude Code 2.1.240 (`claude --help`). Jeder Aufruf:

```
claude -p --model <modell> --output-format json --system-prompt "<system-prompt>\n\nAufgabe:\n<aktionsprompt>"
       --no-session-persistence --strict-mcp-config --tools ""
```

- Der markierte Text geht nur über stdin, als `<text>\n…\n</text>`, UTF-8 ohne BOM.
- Arbeitsordner ist `%TEMP%\ClaudeHotkey\work`, damit keine `CLAUDE.md` oder Projektdateien geladen werden.
- `ANTHROPIC_API_KEY` und `ANTHROPIC_AUTH_TOKEN` werden aus der Umgebung des Prozesses entfernt.
- Ebenso `ANTHROPIC_BASE_URL`, `CLAUDECODE`, `CLAUDE_CODE_*` und `CLAUDE_AGENT_SDK_*`. Diese erbt das Tool, wenn es
  aus einer Claude-Code-Sitzung heraus gestartet wird (z. B. aus der Desktop-App). Dann würde `CLAUDE_CODE_SIMPLE=1`
  den `--bare`-Modus einschalten, der den Abo-Login nie liest, und `ANTHROPIC_BASE_URL` auf den Proxy der Sitzung
  zeigen. `CLAUDE_CONFIG_DIR` bleibt erhalten. Ein Gateway in der `settings.json` von Claude Code wirkt weiterhin.
- `--strict-mcp-config` ohne `--mcp-config` lädt keine MCP-Server (schnellerer Start, keine Werkzeuge).
- `--bare` wird bewusst **nicht** genutzt: Es liest laut Hilfe nur `ANTHROPIC_API_KEY` und nie den Abo-Login.
- Ausgabe: ein JSON-Objekt. Der Antworttext steht in `result`, Fehler erkennt das Tool an `is_error`,
  `api_error_status` (401/403 = Login, 429 = Limit) und am Text in `result`. Beispiel eines echten Fehlerlaufs:
  `{"is_error":true,"terminal_reason":"api_error","result":"Failed to authenticate: OAuth session expired and could not be refreshed", …}`

Erster echter Lauf (31.10.2026, Modell `haiku`): Preflight „Abo aktiv“ (`authMethod=claude.ai`,
`apiKeySource=none`) in 6,1 s, eine Korrektur in 6,0 s. Umlaute und Emojis kamen unverändert zurück.

### Preflight

1. `claude auth status` (kostet nichts): bei `"loggedIn": false` sofort „nicht angemeldet“.
2. Ein kurzer Aufruf mit `--output-format stream-json --verbose`. Die erste Zeile (`type: system`, `subtype: init`)
   enthält `apiKeySource`. Ist der Wert `none`, gilt „Abo aktiv“, sonst erscheint eine Warnung.

### Programm finden

Ein in den Einstellungen gesetzter Pfad gewinnt. Sonst wird im `PATH` zuerst `claude.exe`, dann `claude.cmd`
gesucht. Ein `.cmd` (npm-Installation) wird über `cmd.exe /d /s /c` gestartet; die Argumente werden dafür wie
beim npm-Paket cross-spawn doppelt mit `^` maskiert (`ClaudeExecutable.BuildCommand`).

## Textübernahme

- **Strg+C+C:** Ein Low-Level-Tastaturhook (`Hotkeys/KeyboardHook.cs`) erkennt zweimal Strg+C innerhalb von
  `doubleCopyMs` (Standard 400 ms). Tasten werden nie geschluckt, Kopieren funktioniert weiter normal. Gedrückt
  gehaltenes C (Autorepeat), Strg+Alt+C und von Programmen erzeugte Tastendrücke zählen nicht; jede andere Taste
  dazwischen setzt die Erkennung zurück. Danach wartet das Tool bis zu 600 ms, bis das Programm fertig kopiert hat.
- **Zweiter Hotkey** (Standard Strg+Alt+Leertaste, `RegisterHotKey`): Das Tool wartet, bis die Hotkey-Tasten
  losgelassen sind (sonst käme Strg+Alt+C an), sichert die Zwischenablage, sendet Strg+C per `SendInput`, liest den
  Text und stellt die Zwischenablage wieder her. Ändert sich die Zwischenablage nicht, war nichts markiert.
- **Sichern und Wiederherstellen** (`Clipboard/ClipboardService.cs`): alle lesbaren Formate (Text, RTF, HTML,
  Bilder, Dateien). Formate, die sich nicht lesen lassen, werden übersprungen. Zugriffe werden bis zu zehnmal
  wiederholt, weil andere Programme die Zwischenablage kurz offen halten.
- **Zwischenablageverlauf von Windows:** Der Dienst `cbdhsvc` liest nach jedem Schreiben die Daten per COM aus
  unserem Prozess, während er die Zwischenablage offen hält. Gewartet wird deshalb mit `Thread.Join` (verarbeitet
  COM-Nachrichten) statt `Thread.Sleep`; sonst warten beide Seiten aufeinander und Zugriffe scheitern nach Sekunden.
- `Clipboard.ContainsText`, `GetText` und `ContainsData` von WPF liefern still `false` bzw. `""`, solange ein
  anderer Prozess die Zwischenablage hält. Das Tool liest deshalb über `GetDataObject`, das in diesem Fall eine
  Ausnahme wirft und damit wiederholt wird.

## Entscheidungen und Abweichungen vom Plan

- **Referenz-Screenshot:** `docs/neptun-reference.png` lag in Phase 3 nicht vor. Das Design nutzt die Startwerte aus
  dem Plan; der Abgleich (Design-Abnahme) ist offen.
- **Control-Templates:** In Phase 3 gibt es eigene Templates für Button, TextBox, ScrollBar, ToolTip, ContextMenu,
  MenuItem, Separator und ProgressBar. ComboBox, CheckBox, ListBox und TabControl folgen in Phase 5 mit dem
  Einstellungsdialog, der sie als Erster verwendet.
- **Tasten 1–9** im Popup starten Aktionen; das steht nicht im Plan, spart aber den Griff zur Maus.
- **Popup-Fokus:** Weil das Popup aus einem Hook heraus erscheint, erlaubt Windows ihm den Vordergrund nicht direkt.
  `ForegroundWindow.Activate` hängt sich dafür kurz an die Eingabewarteschlange des aktiven Fensters
  (`AttachThreadInput`).
- **DPI:** Die App ist Per-Monitor-DPI-aware (`app.manifest`), damit Mausposition und Monitor-Arbeitsbereich in
  denselben Pixeln rechnen.
- **Zwischenablage bei Strg+C+C:** Hier bleibt der markierte Text in der Zwischenablage, wie bei DeepL. Der Nutzer
  hat selbst kopiert, und der Inhalt davor lässt sich nicht zuverlässig sichern: Das erste Strg+C hat ihn schon
  überschrieben, bevor das Tool weiß, dass ein zweites folgt. Wiederhergestellt wird beim zweiten Hotkey und
  (Phase 4) nach dem Ersetzen.

- **Solution-Format:** Das installierte SDK 10 erzeugt standardmäßig `.slnx`; angelegt ist bewusst eine klassische
  `ClaudeHotkey.sln`, Zielplattform aller Projekte `net8.0-windows`.
- **Repo-Root:** Die Struktur liegt direkt im Repository-Ordner, ohne zusätzlichen Unterordner `ClaudeHotkey/`.
- **Aktionsprompt:** Er wird an den System-Prompt angehängt (`Aufgabe: …`); stdin enthält nur den markierten Text.
  So gibt es kein Positionsargument, das `--tools` (variadisch) verschlucken könnte.
- **Testroutine:** eigenes Konsolenprojekt `tools/ClaudeHotkey.CliCheck`, weil eine WPF-App keine Konsole hat.
- **Diff-Ansicht (spätere Erweiterung):** Der Plan nennt `difflib`, das gibt es nur in Python; in C# wäre z. B. DiffPlex
  geeignet.
- **Eigene Hotkeys pro Aktion** werden in Phase 5 (Einstellungsdialog) umgesetzt, nicht erst als spätere Erweiterung.

## Bekannte Einschränkungen

- Die Clipboard-Tests benutzen die echte Zwischenablage. Sie sichern den Inhalt vorher und stellen ihn danach wieder
  her; während `dotnet test` sollte man trotzdem nicht kopieren.
- Fenster von Programmen mit Administratorrechten erreicht der zweite Hotkey nicht (kein `SendInput` dorthin),
  solange das Tool selbst ohne Adminrechte läuft. Strg+C+C funktioniert dort, weil der Nutzer selbst kopiert.

- Jeder Aufruf startet einen eigenen Claude-Code-Prozess; einige Sekunden bis zur Antwort sind normal.
- Bei einem Enterprise-Konto (`subscriptionType: enterprise`) laufen die Aufrufe über den Sitz der Organisation.
  Wie sie abgerechnet werden, legt die Organisation fest; die Claude-Code-Startseite kann dabei „API Usage Billing“
  zeigen, obwohl kein API-Key benutzt wird.
- Aufrufe über `claude -p` zählen gegen die Nutzungslimits des Abos. Ist das Limit erreicht, scheitert der Aufruf
  mit einer Meldung.
