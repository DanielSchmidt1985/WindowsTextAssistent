namespace ClaudeHotkey.Core.Startup;

/// <summary>
/// Only one instance per Windows session. A second start does not run its own hooks; it asks the
/// first instance to show its settings (or, with <see cref="Request.Exit"/>, to quit) and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "ClaudeHotkey";

    public enum Request { Activate, Exit }

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _exit;
    private readonly List<RegisteredWaitHandle> _waits = [];
    private readonly bool _owned;

    private SingleInstance(string name, Action onActivate, Action onExit)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out _owned);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}-activate");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}-exit");
        if (!_owned)
            return;
        _waits.Add(ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false));
        _waits.Add(ThreadPool.RegisterWaitForSingleObject(_exit, (_, _) => onExit(), null, Timeout.Infinite, executeOnlyOnce: false));
    }

    public bool IsFirst => _owned;

    /// <summary>
    /// Returns the instance guard. When <see cref="IsFirst"/> is false, the running instance has
    /// already received <paramref name="request"/>; the caller should exit.
    /// </summary>
    /// <param name="onActivate">Called on a thread-pool thread when another start asks this instance to show itself.</param>
    /// <param name="onExit">Called on a thread-pool thread when another start asks this instance to quit.</param>
    public static SingleInstance Acquire(Action onActivate, Action onExit, Request request = Request.Activate, string name = DefaultName)
    {
        var instance = new SingleInstance(name, onActivate, onExit);
        if (!instance.IsFirst)
            (request == Request.Exit ? instance._exit : instance._activate).Set();
        return instance;
    }

    public void Dispose()
    {
        foreach (var wait in _waits)
            wait.Unregister(null);
        if (_owned)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activate.Dispose();
        _exit.Dispose();
    }
}
