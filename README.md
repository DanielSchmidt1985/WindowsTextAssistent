# ClaudeHotkey

Persönliches Windows-Tool: Strg+C+C übernimmt den markierten Text, ein Popup bietet Aktionen wie
Übersetzen oder Verbessern an, die Aktion läuft über die lokale Claude-CLI (`claude -p`) mit dem
vorhandenen Claude-Abo, und das Ergebnis lässt sich per Button ins Quellfenster einsetzen.

Stand: **Phase 6 (Tray, Autostart, Feinschliff)**. Die Auslieferung als einzelne `.exe` (Phase 7) folgt.

## Tray

Das Tool läuft ohne Fenster im Infobereich der Taskleiste. Windows 11 legt neue Icons zuerst in den Überlauf (^);
dauerhaft sichtbar wird es über *Einstellungen → Personalisierung → Taskleiste → Andere Symbole der Taskleiste*.

- **Tooltip und erste Menüzeile** zeigen den Zustand: „Abo aktiv“ oder die Warnung des Preflights, eine fehlerhafte
  `actions.json` und von anderen Programmen belegte Hotkeys. Bei einer Warnung bekommt das Icon einen roten Punkt.
- **Menü (Rechtsklick):** Einstellungen, Logdatei öffnen, Beenden. **Doppelklick** öffnet die Einstellungen.
- **Benachrichtigungen** erscheinen bei belegten Hotkeys, fehlerhafter `actions.json` oder `theme.json` und wenn
  Claude nicht erreichbar ist oder einen API-Key benutzt.
- **Nur eine Instanz:** Ein zweiter Start öffnet die Einstellungen der laufenden Instanz und beendet sich.
  `ClaudeHotkey.exe --exit` beendet eine laufende Instanz (z. B. vor einem Update der `.exe`).

## Logdatei

`%APPDATA%\ClaudeHotkey\log.txt`, ab 1 MB rotiert nach `log.1.txt`. Eine Zeile je Ereignis mit Zeitpunkt, Ereignis,
Aktions-Kennung, Dauer und Code, z. B. `claude  action=improve  ms=6012  code=None`. `AppLog.Write` hat keinen
Parameter für freien Text, und alle Werte werden auf Buchstaben, Ziffern und `-_.+` gekürzt; markierter Text oder
Claudes Antwort können so nicht hineingeraten. Ereignisse: `start`, `preflight`, `capture`, `claude`, `replace`,
`settings`, `autostart`, `config`, `hotkey`, `exit`.

## Autostart

„Mit Windows starten“ in den Einstellungen trägt beim **Speichern** `"<Pfad zur exe>"` unter
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` als `ClaudeHotkey` ein bzw. entfernt den Eintrag. Nur dieser
eine Wert wird angefasst, Adminrechte sind nicht nötig. Der Eintrag wird bewusst nur beim Speichern geschrieben,
nicht bei jedem Start: Sonst würde ein Test-Build aus `bin\Debug` den Eintrag der installierten Kopie umbiegen.
Deshalb den Schalter aus der veröffentlichten `.exe` heraus einschalten.

## Bedienung

Text markieren, **Strg+C+C** oder **Strg+Alt+Leertaste** drücken. Das Popup öffnet sich an der Maus.

- Aktion per Klick oder mit den Tasten **1–9** starten. „Freitext“ fragt zuerst nach einer Anweisung (Enter führt aus).
- Während Claude arbeitet, läuft ein Balken; **Esc** bricht ab (der CLI-Prozess wird beendet) und schließt.
- **Kopieren** legt das Ergebnis in die Zwischenablage und schließt das Popup.
- **Ersetzen** (oder **Enter**) setzt das Ergebnis anstelle der Markierung ins Quellfenster ein. Nur bei Aktionen mit
  `"replace": true`. Danach steht die Zwischenablage wieder so wie vor dem Ersetzen.
- Bei Fehlern steht eine kurze Meldung im Popup, die Rohausgabe der CLI unter „Details“, dazu „Erneut versuchen“.
- Ein neuer Auslöser ersetzt ein offenes Popup und bricht dessen Aufruf ab.

## Einstellungsdialog

Öffnen über das Tray-Menü, einen Doppelklick aufs Tray-Icon oder das Zahnrad im Popup.
Der Dialog schreibt dieselbe `actions.json`, die sich auch von Hand bearbeiten lässt.

- **Aktionen:** Liste links, per Drag-and-drop oder mit ↑/↓ sortierbar; die Reihenfolge ist die der Popup-Buttons.
  Neu, Duplizieren (ohne Hotkey, der wäre doppelt) und Löschen. Rechts Name, Prompt, Modell (Auswahl oder freie
  Eingabe, leer = Standard), eigener Hotkey, Ersetzen-Button, Eingabefeld und Aktiv.
- **Kennung:** Bei neuen Aktionen folgt sie dem Namen („Höflicher“ → `hoeflicher`), nach dem ersten Speichern bleibt
  sie fest. Bestehende Aktionen behalten ihre Kennung immer.
- **Eigener Hotkey:** Feld anklicken und die Kombination drücken, Entf oder Rücktaste löscht. Solange das Feld den
  Fokus hat, sind die globalen Hotkeys des Tools ausgesetzt, sonst käme eine schon belegte Kombination nie an. Der
  Hotkey kopiert den markierten Text, öffnet das Popup und führt die Aktion sofort aus.
- **Doppeldruck** (z. B. Strg+X+X): im Hotkey-Feld dieselbe Kombination zweimal schnell drücken (600 ms). Gilt für
  Aktions-Hotkeys und den zweiten Hotkey; das Zeitfenster beim Auslösen ist `doubleCopyMs`.
- **Zweiter Hotkey** ist optional: Entf im Feld, speichern, dann ist keiner registriert.
- **Testen:** Beispieltext (und bei „Eingabefeld im Popup“ die Eingabe) eintragen; die Aktion läuft mit den noch nicht
  gespeicherten Werten, auch mit dem geänderten System-Prompt aus „Allgemein“.
- **Allgemein:** Zeitfenster für Strg+C+C, zweiter Hotkey, Standardmodell, Timeout, Wartezeit beim Einfügen,
  Pfad zur CLI, System-Prompt, Autostart.
- **Speichern** (oder Strg+S) prüft alles, schreibt atomar und wirkt sofort, auch für Hotkeys. Fehler stehen rot am
  Feld, Aktionen mit Fehlern haben einen roten Punkt in der Liste, und die Datei bleibt dann unverändert.
- **Schließen** mit ungespeicherten Änderungen fragt nach.

Wird `actions.json` von Hand geändert, übernimmt das Tool die neue Fassung beim nächsten Auslösen, einschließlich der
Hotkeys.

## Konfiguration

`%APPDATA%\ClaudeHotkey\actions.json` entsteht beim ersten Start aus `src/ClaudeHotkey.Core/Config/actions.default.json`
(eingebettet in die DLL). Die Datei wird vor jedem Popup neu gelesen, wenn sie sich geändert hat; alle Änderungen
gelten ohne Neustart.

- Erlaubt sind Kommentare (`//`) und Kommas am Ende.
- Geprüft wird: eindeutige `id`, nicht leerer `prompt`, `askInput` nur mit `{input}` im Prompt, gültige und nicht
  doppelt vergebene Hotkeys, Wertebereiche für `doubleCopyMs` (100–2000) und `timeoutSeconds` (5–600).
- Ist die Datei fehlerhaft, bleibt die zuletzt gültige Konfiguration aktiv; das Tray meldet die Fehler.
- Zusätzlich zum Plan: `settings.autostart`, `settings.claudePath` und pro Aktion `hotkey` und `enabled`
  (für den Einstellungsdialog). `enabled: false` blendet eine Aktion im Popup aus.

### Design anpassen

Alle Farben stehen als Token in `src/ClaudeHotkey/Themes/Theme.Neptun.xaml`. Ohne Neubau lassen sie sich in
`%APPDATA%\ClaudeHotkey\theme.json` überschreiben, z. B. `{ "AccentBrush": "#3794FF" }`. Neben den Tokens aus dem
Plan gibt es `HoverBrush`, `PressedBrush` und `SelectionBrush`.

Zum Vergleichen mit dem Referenz-Screenshot öffnet `ClaudeHotkey.exe --demo <zustand>` das Popup mit Beispieltext,
ohne Hook und ohne Claude-Aufruf. Zustände: `choose`, `input`, `running`, `result`, `error`, `nocapture`,
`replacefail`. Den Einstellungsdialog zeigen `settings`, `settings-general`, `settings-errors` und `settings-test`;
sie arbeiten auf einer Kopie in `%TEMP%\ClaudeHotkey-demo`, nie auf der echten `actions.json`.

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
  gehaltenes C (Autorepeat), weitere Modifier (Strg+Umschalt+C, Strg+Alt+C) und von Programmen erzeugte
  Tastendrücke zählen nicht; jede andere Taste dazwischen setzt die Erkennung zurück. Danach wartet das Tool bis zu
  600 ms, bis das Programm fertig kopiert hat.
- **Zweiter Hotkey** (Standard Strg+Alt+Leertaste, optional, auch als Doppeldruck): Das Tool wartet, bis die Hotkey-Tasten
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

## Ersetzen

`Windows/TextReplacer.cs`, Ablauf:

1. Das Vordergrundfenster wird schon beim Auslösen gemerkt (vor dem Popup).
2. Zwischenablage sichern, Ergebnis als reinen Text hineinlegen, Popup ausblenden.
3. Quellfenster aktivieren und bis zu 1 s warten, bis es wirklich vorne ist.
4. Warten, bis keine Modifier-Taste mehr gedrückt ist, dann Strg+V per `SendInput`.
5. `pasteDelayMs` warten (Standard 500 ms), damit das Programm die Zwischenablage lesen kann, dann den alten Inhalt
   zurückholen. Hat in der Zwischenzeit jemand etwas Neues kopiert, bleibt das stehen.

Scheitert ein Schritt, kommt das Popup mit einer Meldung zurück; das Ergebnis bleibt sichtbar und kopierbar. Ließ
sich das Fenster nicht aktivieren, bleibt das Ergebnis zusätzlich in der Zwischenablage, um es selbst einzufügen.

Fügt ein langsames Programm noch den alten Inhalt ein, `pasteDelayMs` in `actions.json` erhöhen (100–5000).

## Doppeldruck-Hotkeys

In `actions.json` steht die Taste zweimal: `"hotkey": "ctrl+x+x"`, `"secondHotkey": "ctrl+alt+q+q"`.
`RegisterHotKey` kennt keinen Doppeldruck; diese Kombinationen erkennt deshalb derselbe Low-Level-Hook wie Strg+C+C
(`Hotkeys/DoublePressDetector.cs`). Die Modifier müssen genau stimmen, gehaltene Tasten zählen nicht doppelt.

- **Der erste Druck kommt beim Programm an.** Der Hook schluckt keine Tasten, denn beim ersten Druck weiß er noch
  nicht, ob ein zweiter folgt. Deshalb Kombinationen wählen, deren einfacher Druck nichts Schlimmes tut.
- **Strg+X+X** schneidet die Markierung beim ersten Druck aus, das Tool nimmt den Text dann aus der Zwischenablage
  (wie bei Strg+C+C). „Ersetzen“ setzt das Ergebnis an der Stelle ein. Schließt man das Popup ohne Ersetzen, fehlt
  der Text im Dokument; Strg+Z oder Strg+V holt ihn zurück.
- Bei allen anderen Tasten kopiert das Tool nach dem zweiten Druck selbst (Strg+C) und stellt die Zwischenablage
  danach wieder her, wie beim zweiten Hotkey.
- Vergeben sind: Strg+C+C (eingebaut) und damit auch Strg+C allein, das sonst das normale Kopieren abfangen würde.
  Einfacher und doppelter Druck derselben Tasten (Strg+X und Strg+X+X) schließen sich gegenseitig aus.
- Doppeldrucke können nicht „von einem anderen Programm belegt“ sein, weil nur der Hook des Tools sie sieht.

## Entscheidungen und Abweichungen vom Plan

- **`pasteDelayMs`:** neue Einstellung, nicht im Plan. Ein Signal, wann ein Programm nach Strg+V die Zwischenablage
  gelesen hat, gibt es nicht zuverlässig; der Windows-Zwischenablageverlauf öffnet sie zur selben Zeit ebenfalls.
  Deshalb eine feste, einstellbare Wartezeit.
- **Referenz-Screenshot:** `docs/neptun-reference.png` lag in Phase 3 nicht vor. Das Design nutzt die Startwerte aus
  dem Plan; der Abgleich (Design-Abnahme) ist offen.
- **Control-Templates:** Alle Controls aus dem Plan haben eigene Templates (Button, TextBox, ComboBox, CheckBox als
  Schalter, ListBox, ScrollBar, TabControl, ToolTip, ContextMenu), dazu ProgressBar, MenuItem und Separator.
  Ausnahme: Die Rückfrage beim Schließen mit ungespeicherten Änderungen ist eine Windows-MessageBox und deshalb hell.
- **Eigene Hotkeys pro Aktion** sind in Phase 5 umgesetzt, nicht erst als spätere Erweiterung.
- **Autostart** wird nur beim Speichern angewendet, nicht bei jedem Start (siehe „Autostart“).
- **H.NotifyIcon.Wpf 2.3.2** statt der neuesten 2.4.1: 2.4.1 enthält nur Builds für `net10.0-windows` und `net462`,
  für `net8.0-windows` würde der .NET-Framework-Build genommen. 2.3.2 hat einen eigenen .NET-8-Build.
- **Kein Effizienzmodus:** `TaskbarIcon.ForceCreate(false)`. Mit dem Standardwert `true` drosselt Windows den Prozess,
  und ein gedrosselter Low-Level-Tastaturhook antwortet zu langsam und wird von Windows entfernt.
- **`--exit`:** zusätzlicher Startparameter, nicht im Plan, für Updates und Skripte.
- **Statusfenster entfernt:** Es diente seit Phase 2 als Ersatz fürs Tray. Was es zeigte, steht jetzt im Tray-Tooltip,
  in Benachrichtigungen und in der Logdatei.
- **Speichern schließt nicht:** Der Dialog bleibt offen, damit sich Prompts nacheinander abstimmen lassen.
- **Auslöser in eigenen Fenstern** (Popup, Einstellungen) werden ignoriert, damit Kopieren dort kein neues Popup öffnet.
- **Modellauswahl:** Vorgeschlagen werden die Aliasse `haiku`, `sonnet`, `opus` und `fable` (laut `claude --help`);
  freie Eingabe eines vollen Modellnamens ist möglich.
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
