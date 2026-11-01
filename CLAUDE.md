# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ClaudeHotkey: a personal Windows tray tool (C# 12, .NET 8, WPF). A hotkey (default Ctrl+C+C) grabs the selected
text, a popup offers actions (translate, improve, …), the action runs through the local Claude CLI (`claude -p`)
on the user's subscription, and "Ersetzen" pastes the result back into the source window. `README.md` (German) is
the detailed reference for behaviour and design decisions; keep it updated with every change.

## Commands

```
dotnet build ClaudeHotkey.sln
dotnet test tests/ClaudeHotkey.Tests
dotnet test tests/ClaudeHotkey.Tests --filter "FullyQualifiedName~DoublePressDetectorTests"   # one class
dotnet test tests/ClaudeHotkey.Tests --filter "FullyQualifiedName~Several_formats"            # one test
dotnet run --project tools/ClaudeHotkey.CliCheck -- [--model sonnet] [--feedback "<instr>"] "Text"  # real CLI call
src/ClaudeHotkey/bin/Debug/net8.0-windows/ClaudeHotkey.exe --demo <state> [scale%]               # popup/settings without hooks or CLI
ClaudeHotkey.exe --exit                                                                          # quit the running instance
dotnet publish src/ClaudeHotkey -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Demo states (see `App.ShowDemo`): `choose input running result error nocapture replacefail feedback settings
settings-general settings-errors settings-test`, plus optional `[scale] [dark|light] [de|en]` in any order. Demos
use copies in `%TEMP%`, never the real config. They are the way to check UI changes visually: start, screenshot,
kill. Check both themes and both languages. The settings window is not topmost and may open on another monitor:
find it via `EnumWindows` by PID and capture its rect.

- **The running app locks `bin\Debug`.** Before building for a hand-over: `ClaudeHotkey.exe --exit`, build, then
  start the new build yourself (the user wants this done for them). For builds/tests while it runs, use a separate
  output dir: `dotnet test tests/ClaudeHotkey.Tests -o <scratch dir>`.
- **Gate commits on test results.** Several tests use the real clipboard and real processes; run the suite more
  than once after touching clipboard code.
- **Real CLI calls from inside a Claude Code session fail as "not logged in"**: the shell inherits `CLAUDE_CODE_*`
  (e.g. `CLAUDE_CODE_SIMPLE` → `--bare`) and `ANTHROPIC_BASE_URL`. `ClaudeCli` strips them for its child processes;
  for manual `claude` calls, unset them first (e.g. in bash:
  `unset $(env | grep -oE '^(CLAUDE_CODE_[A-Z0-9_]*|CLAUDECODE|ANTHROPIC_[A-Z0-9_]*|CLAUDE_AGENT_SDK_[A-Z0-9_]*)=' | tr -d '=')`).
- Verify CLI flags with `claude --help` instead of from memory (plan requirement).

## Architecture

`src/ClaudeHotkey.Core` holds all logic without UI so it can be tested; `src/ClaudeHotkey` is the WPF app;
`tests/ClaudeHotkey.Tests` (xUnit) references both. `App.xaml.cs` wires everything together.

Flow of one use:
1. **Trigger** — single-press hotkeys via `RegisterHotKey` (`Hotkeys/GlobalHotkeys`), double presses (Ctrl+C+C,
   Alt+K+K, …) via the low-level keyboard hook (`Hotkeys/KeyboardHook` + pure `DoublePressDetector`). The hook
   never swallows keys. `App.RegisterHotkeys` maps every gesture to the main hotkey or an action id and re-registers
   live after saving or when `actions.json` was edited by hand.
2. **Capture** (`Clipboard/TextCapture`) — Ctrl+C+C and Ctrl+X+X (`HotkeyGesture.AppCopiesItself`): the app already
   copied, read the clipboard. Any other gesture: wait for modifiers to be released, back up clipboard, `SendInput`
   Ctrl+C, read, restore.
3. **Popup** (`Views/PopupWindow`, `ViewModels/PopupViewModel`) at the mouse (`Windows/ForegroundWindow`: placement
   in physical pixels, app is per-monitor DPI aware; activation via `AttachThreadInput`).
4. **Claude** (`Claude/ClaudeCli`) — `claude -p --model … --output-format stream-json --verbose --include-partial-messages --system-prompt "<system prompt>\n\nAufgabe:\n<action prompt>" --settings '{"disableAllHooks":true}' --no-session-persistence --strict-mcp-config --tools ""`;
   selected text only over stdin as `<text>…</text>`; empty temp working dir; API-key and Claude-Code session env
   vars removed. `--tools ""` must stay last (variadic). Actions with `feedback` add `--json-schema` and read
   `structured_output` {text, feedback} from the final `type: result` line. `PartialTextReader` turns the
   `stream_event` lines into the text so far (popup shows it while running; `RunClaudeStreaming`). `IProcessRunner` is the test seam; `claude.cmd` goes through `cmd.exe` with
   cross-spawn style `^` escaping.
5. **Replace** (`Windows/TextReplacer`) — result into clipboard, re-activate source window, Ctrl+V, wait
   `pasteDelayMs`, restore the previous clipboard unless the user copied something meanwhile.

Configuration and state, all in `%APPDATA%\ClaudeHotkey`:
- `actions.json` (`Config/ActionConfig`, `ActionConfigStore`): settings + actions. Created from the embedded
  `Config/actions.default.json` (German) or `actions.default-en.json` (keep both in sync; a test compares their
  ids/hotkeys/models). Reloaded on every trigger by mtime; a broken file keeps the last valid config.
  `ValidateDetailed` returns errors per action/field for the settings dialog. Atomic writes (tmp + move).
  `settings.language` (system/de/en) and `settings.theme` (system/dark/light).
- `ui.json` (`Config/UiState`): scale, popup width, settings window bounds — separate so popup dragging never
  races with the dialog saving `actions.json`.
- `theme.json` (optional): overrides `*Brush` tokens of the active palette at runtime.

Language: `Core/Localization/Lang` — every user-visible text is `Lang.T("deutsch", "english")` at the call site;
window texts live in `src/ClaudeHotkey/Localization/Ui.cs` and XAML uses `{x:Static l:Ui.Name}`. "system" = German
for a German display language, English otherwise. Tests run in German (`TestSetup` module initializer); never
switch `Lang.Current` inside a test (classes run in parallel). Theme: styles in `Themes/Theme.Neptun.xaml` use only
`DynamicResource` tokens from `Colors.Dark.xaml`/`Colors.Light.xaml` (same keys, tested); `ThemeManager` copies a
palette into the app resources and follows the Windows app mode via `SystemEvents.UserPreferenceChanged`.
- `log.txt` (`Logging/AppLog`): only event/action id/duration/code; the API has no free-text parameter on purpose.

Other pieces: tray (`Views/TrayIcon`, H.NotifyIcon.Wpf **2.3.2** — 2.4.x has no net8 build; `ForceCreate(false)`
because efficiency mode throttles the process and Windows then drops the keyboard hook), single instance with
activate/exit signals (`Startup/SingleInstance`), autostart via HKCU Run only on "Speichern" (`Startup/Autostart`,
not on every start so a debug build never redirects the installed entry).

## Pitfalls found the hard way

- **Clipboard:** the clipboard history service (`cbdhsvc`) reads our data over COM while holding the clipboard.
  Retry waits must pump COM (`Thread.CurrentThread.Join`, never `Thread.Sleep`). WPF `ContainsText`/`GetText`/
  `ContainsData` return false/"" instead of throwing when the clipboard is busy — read via `GetDataObject`.
  `Backup` must not skip a format on a *transient* error (`ClipboardService.IsTransient`) or HTML/RTF/Unicode get
  lost. Test setup that touches the clipboard directly needs the same retry.
- **WPF resources:** Freezables in `Application.Resources` are frozen on insert (even created in code, even with
  `PresentationOptions:Freeze="False"`). The shared `UiScale` ScaleTransform is therefore replaced, not mutated, and
  windows bind it with `DynamicResource`.
- **Templates:** a `Loaded` EventTrigger in a template of an element that starts collapsed fires before the template
  exists and crashes the app (busy bar) — use a property trigger on `IsVisible`. No implicit `TextBlock` style (it
  overrides button text colours); windows set `Foreground` instead. XML comments in `.csproj` must not contain `--`.
- **Hotkeys:** a single Ctrl+C/X/V via `RegisterHotKey` would steal the key from all apps — validation rejects it
  (`BlocksClipboardKeys`). For double presses the first press reaches the app.
- **ComboBox with records:** the themed ComboBox shows the selected item via `ToString()` even with
  `DisplayMemberPath`; `Choice` overrides `ToString()` for that.
- **Embedded resources named `*.en.json`** become satellite assemblies (culture "en"); hence `actions.default-en.json`.
- **Files with a BOM** (`App.xaml.cs`): a sed rule anchored on the first line (`^using …`) does not match.
- **Process.MainWindowHandle** is 0 for the popup (no taskbar entry); find windows via `EnumWindows` by PID.

## Conventions

- UI texts: German and English (both, via `Lang.T`). README, commit messages: German. Code, identifiers and code
  comments: English.
- Never `sed -i` over a `grep -rl` result without excluding `bin`/`obj`: it rewrites dll/pdb files too.
- Commit after each finished step; commit messages end with the `Co-Authored-By` line given by the session.
- Remote: `origin` = https://github.com/DanielSchmidt1985/WindowsTextAssistent (`main`). Push when asked.
- Releases: bump `<Version>` in `src/ClaudeHotkey/ClaudeHotkey.csproj`, publish from a clean, pushed commit, tag
  `vX.Y.Z`, then `"C:\Program Files\GitHub CLI\gh.exe" release create vX.Y.Z publish\ClaudeHotkey.exe --notes-file …`
  (gh is logged in as DanielSchmidt1985). Single file is uncompressed on purpose (114 vs 222 MB working set).
- Installed copy: `%LOCALAPPDATA%\Programs\ClaudeHotkey\ClaudeHotkey.exe` (autostart points there). Update it with
  `--exit`, copy, start.
- Don't change the user's registry, `actions.json` or other personal config without saying so; back up before
  editing `actions.json` by hand (`actions.json.<reason>.bak`).
- Open item: design check against `docs/neptun-reference.png` (screenshot not provided yet).
