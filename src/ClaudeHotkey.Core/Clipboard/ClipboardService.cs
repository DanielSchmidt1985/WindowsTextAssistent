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
    /// Saves every format that can be read. Formats that throw (e.g. some metafiles or delayed
    /// rendering by a closed app) are skipped; text, RTF, HTML, images and files survive.
    /// </summary>
    public ClipboardSnapshot Backup() => Retry(() =>
    {
        var formats = new List<KeyValuePair<string, object>>();
        var data = WpfClipboard.GetDataObject();
        if (data is null)
            return new ClipboardSnapshot(formats);

        foreach (var format in data.GetFormats(autoConvert: false))
        {
            try
            {
                if (data.GetData(format, autoConvert: false) is { } value)
                    formats.Add(new(format, value));
            }
            catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or NotImplementedException)
            {
                // Not every format survives a round trip; skip it.
            }
        }
        return new ClipboardSnapshot(formats);
    });

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
