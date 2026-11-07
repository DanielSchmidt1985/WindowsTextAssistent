using System.Diagnostics;
using System.Windows.Threading;
using ClaudeHotkey.Core.Localization;
using ClaudeHotkey.Core.Update;

namespace ClaudeHotkey;

/// <summary>
/// Self-update of the installed copy: check GitHub a minute after start and every 6 hours, download
/// and verify in the background, swap and restart once nothing is open. See <see cref="Updater"/>.
/// </summary>
public partial class App
{
    private const string UpdatedArgument = "--updated";
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan IdleRetry = TimeSpan.FromMinutes(1);

    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _idleTimer;
    private bool _updateRunning;
    private bool _updateReady;

    private static string ExePath => Environment.ProcessPath ?? "";

    private static Version CurrentVersion => Updater.Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static bool IsInstalledCopy =>
        Updater.IsInstalledCopy(ExePath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// After a swap the old process starts the new exe with "--updated &lt;pid&gt;". The new one must
    /// wait until the old one is gone, otherwise the single-instance check sends it away.
    /// </summary>
    private static void WaitForPreviousInstance(string[] args)
    {
        if (args is not [UpdatedArgument, var pidText, ..] || !int.TryParse(pidText, out var pid))
            return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
    }

    private void StartUpdates(string[] args)
    {
        if (IsInstalledCopy)
            Updater.CleanUp(ExePath);
        if (args.Contains(UpdatedArgument))
        {
            var version = CurrentVersion.ToString(3);
            _log.Write("update", version, code: "Installed");
            _tray?.Notify("ClaudeHotkey", Lang.T($"Auf Version {version} aktualisiert.", $"Updated to version {version}."), warning: false);
        }

        _updateTimer = new DispatcherTimer { Interval = FirstUpdateCheck };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = UpdateInterval;
            await CheckForUpdateAsync(manual: false);
        };
        _updateTimer.Start();
    }

    /// <param name="manual">From the tray menu: runs even with autoUpdate off and reports every outcome.</param>
    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateRunning)
            return;
        if (!manual && !_config.Current.Settings.AutoUpdate)
            return;
        if (!IsInstalledCopy)
        {
            if (manual)
                NotifyUpdate(Lang.T("Nur die installierte Kopie unter %LOCALAPPDATA%\\Programs\\ClaudeHotkey aktualisiert sich selbst.",
                    "Only the installed copy under %LOCALAPPDATA%\\Programs\\ClaudeHotkey updates itself."), warning: false);
            return;
        }
        if (_updateReady)
        {
            TryApplyUpdate();
            return;
        }

        _updateRunning = true;
        try
        {
            using var transport = new GitHubTransport(CurrentVersion);
            var updater = new Updater(transport, ExePath, CurrentVersion);
            var (outcome, release) = await updater.CheckAsync(CancellationToken.None);
            if (release is null)
            {
                _log.Write("update", code: outcome.ToString());
                if (manual)
                    NotifyUpdate(outcome == UpdateOutcome.UpToDate
                        ? Lang.T($"Version {CurrentVersion.ToString(3)} ist aktuell.", $"Version {CurrentVersion.ToString(3)} is up to date.")
                        : Lang.T("GitHub war nicht erreichbar, später noch einmal.", "Could not reach GitHub, try again later."), outcome != UpdateOutcome.UpToDate);
                return;
            }

            var version = release.Version.ToString(3);
            if (manual)
                NotifyUpdate(Lang.T($"Version {version} wird geladen …", $"Downloading version {version} …"), warning: false);
            var downloaded = await updater.DownloadAsync(release, CancellationToken.None);
            _log.Write("update", version, code: downloaded == UpdateOutcome.Ready ? "Downloaded" : downloaded.ToString());
            if (downloaded != UpdateOutcome.Ready)
            {
                if (manual)
                    NotifyUpdate(Lang.T($"Version {version} ließ sich nicht laden ({downloaded}).", $"Version {version} could not be downloaded ({downloaded})."), warning: true);
                return;
            }
            _updateReady = true;
        }
        catch (Exception ex)
        {
            _log.Write("update", code: ex.GetType().Name);
            if (manual)
                NotifyUpdate(Lang.T("Die Suche nach Updates ist fehlgeschlagen.", "Checking for updates failed."), warning: true);
            return;
        }
        finally
        {
            _updateRunning = false;
        }
        TryApplyUpdate();
    }

    /// <summary>Nothing the user is working with: no popup, no capture, no settings dialog with possibly unsaved changes.</summary>
    private bool IsIdle => _popup is null && !_capturing && _settings is null;

    private void TryApplyUpdate()
    {
        if (!_updateReady)
            return;
        if (!IsIdle)
        {
            _idleTimer ??= CreateIdleTimer();
            _idleTimer.Start();
            return;
        }

        var swapped = Updater.Swap(ExePath);
        _log.Write("update", code: swapped == UpdateOutcome.Ready ? "Swapped" : swapped.ToString());
        _updateReady = false;
        if (swapped != UpdateOutcome.Ready)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, $"{UpdatedArgument} {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            // The new exe is in place; it runs from the next start (autostart) on.
            _log.Write("update", code: "RestartFailed-" + ex.GetType().Name);
            return;
        }
        Shutdown();
    }

    private DispatcherTimer CreateIdleTimer()
    {
        var timer = new DispatcherTimer { Interval = IdleRetry };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryApplyUpdate();
        };
        return timer;
    }

    private void NotifyUpdate(string message, bool warning) => _tray?.Notify("Update", message, warning);

    private void StopUpdates()
    {
        _updateTimer?.Stop();
        _idleTimer?.Stop();
    }
}
