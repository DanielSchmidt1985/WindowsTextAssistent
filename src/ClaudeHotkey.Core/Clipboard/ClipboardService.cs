using System.Runtime.InteropServices;
using System.Windows;
using ClaudeHotkey.Core.Interop;
using WpfClipboard = System.Windows.Clipboard;

namespace ClaudeHotkey.Core.Clipboard;

/// <summary>Copy of the clipboard content in all formats that could be read.</summary>
public sealed class ClipboardSnapshot
{
    internal ClipboardSnapshot(IReadOnlyList<KeyValuePair<string, object>> formats) => Formats = formats;

    public IReadOnlyList<KeyValuePair<string, object>> Formats { get; }

    public bool IsEmpty => Formats.Count == 0;
}

/// <summary>
/// Clipboard access with retries, because another program may hold the clipboard open for a
/// moment. All methods must be called on an STA thread (the WPF UI thread).
/// </summary>
public sealed class ClipboardService
{
    private const int Attempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(30);

    public static uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    // Not Clipboard.ContainsText/GetText: they return false/"" instead of throwing while another
    // process holds the clipboard, so Retry would never see the failure.
    public string? GetText() => Retry(() =>
    {
        var data = WpfClipboard.GetDataObject();
        if (data is null || !data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true))
            return null;
        return data.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
    });

    public void SetText(string text) => Retry(() =>
    {
        WpfClipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true);
        return true;
    });

    /// <summary>
    /// Saves every format that can be read. Formats that cannot be read at all (e.g. some
    /// metafiles or delayed rendering by a closed app) are skipped; text, RTF, HTML, images and
    /// files survive.
    /// </summary>
    /// <remarks>
    /// A format that fails only because another process (usually the clipboard history service)
    /// holds the clipboard for a moment must not be skipped: the snapshot would silently lose
    /// e.g. "HTML Format" or "UnicodeText" (emojis then come back as "??"). Such failures restart
    /// the whole backup; only on the last attempt is a partial snapshot accepted.
    /// </remarks>
    public ClipboardSnapshot Backup()
    {
        for (var attempt = 1; ; attempt++)
        {
            var strict = attempt < Attempts;
            try
            {
                return ReadSnapshot(strict);
            }
            catch (Exception ex) when (strict && (ex is TransientClipboardException || IsTransient(ex)))
            {
                Thread.CurrentThread.Join(RetryDelay); // see Retry for why not Thread.Sleep
            }
        }
    }

    private static ClipboardSnapshot ReadSnapshot(bool strict)
    {
        var formats = new List<KeyValuePair<string, object>>();
        var data = WpfClipboard.GetDataObject();
        if (data is null)
            return new ClipboardSnapshot(formats);

        foreach (var format in data.GetFormats(autoConvert: false))
        {
            object? value;
            try
            {
                value = data.GetData(format, autoConvert: false);
            }
            catch (Exception ex) when (strict && IsTransient(ex))
            {
                throw new TransientClipboardException(format, ex);
            }
            catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or NotImplementedException)
            {
                continue; // this format does not survive a round trip; skip it
            }

            if (value is not null)
                formats.Add(new(format, value));
            else if (strict)
                throw new TransientClipboardException(format, null); // listed but not delivered: try again
        }
        return new ClipboardSnapshot(formats);
    }

    /// <summary>The clipboard is busy, not the format broken: worth another try.</summary>
    internal static bool IsTransient(Exception ex) => ex is ExternalException { HResult: CLIPBRD_E_CANT_OPEN or RPC_E_CALL_REJECTED or RPC_E_SERVERCALL_RETRYLATER };

    private const int CLIPBRD_E_CANT_OPEN = unchecked((int)0x800401D0);
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);

    private sealed class TransientClipboardException(string format, Exception? inner)
        : Exception($"Clipboard format '{format}' could not be read right now.", inner);

    public void Restore(ClipboardSnapshot snapshot) => Retry(() =>
    {
        if (snapshot.IsEmpty)
        {
            WpfClipboard.Clear();
            return true;
        }
        var data = new DataObject();
        foreach (var (format, value) in snapshot.Formats)
        {
            try { data.SetData(format, value, autoConvert: false); }
            catch (Exception ex) when (ex is COMException or ExternalException) { }
        }
        WpfClipboard.SetDataObject(data, copy: true);
        return true;
    });

    /// <summary>Waits until the clipboard changed after <paramref name="sequence"/>. False on timeout.</summary>
    public static async Task<bool> WaitForChangeAsync(uint sequence, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (SequenceNumber == sequence)
        {
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(15).ConfigureAwait(true);
        }
        // Some apps set several formats one after another; give them a moment to finish.
        await Task.Delay(30).ConfigureAwait(true);
        return true;
    }

    private static T Retry<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is COMException or ExternalException && attempt < Attempts)
            {
                // Not Thread.Sleep: the clipboard history service (cbdhsvc) reads our data object
                // over COM while holding the clipboard open. A blocking sleep would not answer it,
                // and both sides would wait for each other. Join on an STA thread pumps COM messages.
                Thread.CurrentThread.Join(RetryDelay);
            }
        }
    }
}
