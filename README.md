# ClaudeHotkey

Persönliches Windows-Tool: Strg+C+C übernimmt den markierten Text, ein Popup bietet Aktionen wie
Übersetzen oder Verbessern an, die Aktion läuft über die lokale Claude-CLI (`claude -p`) mit dem
vorhandenen Claude-Abo, und das Ergebnis lässt sich per Button ins Quellfenster einsetzen.

Stand: **Phase 2 (Hotkey und Textübernahme)**. Popup, Ersetzen und Tray folgen in den nächsten Phasen.
Bis dahin zeigt die App ein vorläufiges Testfenster, das jede Textübernahme auflistet; Schließen beendet das Tool.

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

## Entscheidungen und Abweichungen vom Plan

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
