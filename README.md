# ClaudeHotkey

Persönliches Windows-Tool: Strg+C+C übernimmt den markierten Text, ein Popup bietet Aktionen wie
Übersetzen oder Verbessern an, die Aktion läuft über die lokale Claude-CLI (`claude -p`) mit dem
vorhandenen Claude-Abo, und das Ergebnis lässt sich per Button ins Quellfenster einsetzen.

Stand: **Phase 1 (Gerüst und CLI-Kern)**. Hotkey, Popup und Tray folgen in den nächsten Phasen.

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

### Preflight

1. `claude auth status` (kostet nichts): bei `"loggedIn": false` sofort „nicht angemeldet“.
2. Ein kurzer Aufruf mit `--output-format stream-json --verbose`. Die erste Zeile (`type: system`, `subtype: init`)
   enthält `apiKeySource`. Ist der Wert `none`, gilt „Abo aktiv“, sonst erscheint eine Warnung.

### Programm finden

Ein in den Einstellungen gesetzter Pfad gewinnt. Sonst wird im `PATH` zuerst `claude.exe`, dann `claude.cmd`
gesucht. Ein `.cmd` (npm-Installation) wird über `cmd.exe /d /s /c` gestartet; die Argumente werden dafür wie
beim npm-Paket cross-spawn doppelt mit `^` maskiert (`ClaudeExecutable.BuildCommand`).

## Entscheidungen und Abweichungen vom Plan

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

- Jeder Aufruf startet einen eigenen Claude-Code-Prozess; einige Sekunden bis zur Antwort sind normal.
- Aufrufe über `claude -p` zählen gegen die Nutzungslimits des Abos. Ist das Limit erreicht, scheitert der Aufruf
  mit einer Meldung.
