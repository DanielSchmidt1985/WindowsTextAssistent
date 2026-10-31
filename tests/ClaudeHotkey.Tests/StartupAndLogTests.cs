using ClaudeHotkey.Core.Logging;
using ClaudeHotkey.Core.Startup;

namespace ClaudeHotkey.Tests;

public class AutostartTests
{
    private const string Exe = @"C:\Tools\ClaudeHotkey\ClaudeHotkey.exe";

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public int Writes { get; private set; }

        public string? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string value)
        {
            Values[name] = value;
            Writes++;
        }

        public void Delete(string name)
        {
            Values.Remove(name);
            Writes++;
        }
    }

    [Fact]
    public void Enabling_writes_quoted_path()
    {
        var key = new FakeRunKey();

        Assert.True(new Autostart(key).Apply(true, Exe));

        Assert.Equal($"\"{Exe}\"", key.Values["ClaudeHotkey"]);
    }

    [Fact]
    public void Enabling_twice_writes_once()
    {
        var key = new FakeRunKey();
        var autostart = new Autostart(key);
        autostart.Apply(true, Exe);

        Assert.False(autostart.Apply(true, Exe));
        Assert.Equal(1, key.Writes);
        Assert.True(autostart.IsEnabledFor(Exe));
    }

    [Fact]
    public void Moved_exe_updates_entry()
    {
        var key = new FakeRunKey();
        key.Values["ClaudeHotkey"] = "\"C:\\Alt\\ClaudeHotkey.exe\"";

        Assert.True(new Autostart(key).Apply(true, Exe));
        Assert.Equal($"\"{Exe}\"", key.Values["ClaudeHotkey"]);
    }

    [Fact]
    public void Disabling_removes_only_our_entry()
    {
        var key = new FakeRunKey();
        key.Values["ClaudeHotkey"] = $"\"{Exe}\"";
        key.Values["OneDrive"] = "onedrive.exe";

        Assert.True(new Autostart(key).Apply(false, Exe));

        Assert.False(key.Values.ContainsKey("ClaudeHotkey"));
        Assert.True(key.Values.ContainsKey("OneDrive"));
    }

    [Fact]
    public void Disabled_without_entry_changes_nothing()
    {
        var key = new FakeRunKey();

        Assert.False(new Autostart(key).Apply(false, Exe));
        Assert.Equal(0, key.Writes);
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void Second_instance_is_refused_and_activates_the_first()
    {
        var name = $"ClaudeHotkeyTest-{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        using var first = SingleInstance.Acquire(activated.Set, exited.Set, name: name);

        using var second = SingleInstance.Acquire(() => { }, () => { }, name: name);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(exited.IsSet);
    }

    [Fact]
    public void Exit_request_reaches_the_first_instance()
    {
        var name = $"ClaudeHotkeyTest-{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        using var first = SingleInstance.Acquire(activated.Set, exited.Set, name: name);

        using var second = SingleInstance.Acquire(() => { }, () => { }, SingleInstance.Request.Exit, name);

        Assert.True(exited.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(activated.IsSet);
    }

    [Fact]
    public void Name_is_free_again_after_dispose()
    {
        var name = $"ClaudeHotkeyTest-{Guid.NewGuid():N}";
        SingleInstance.Acquire(() => { }, () => { }, name: name).Dispose();

        using var again = SingleInstance.Acquire(() => { }, () => { }, name: name);

        Assert.True(again.IsFirst);
    }
}

public class AppLogTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("claudehotkey-log");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Writes_event_action_duration_and_code_on_one_line()
    {
        var log = new AppLog(Path.Combine(_dir.FullName, AppLog.FileName));

        log.Write("claude", "improve", TimeSpan.FromMilliseconds(6012.7), "None");

        var line = Assert.Single(File.ReadAllLines(log.FilePath));
        Assert.EndsWith("  claude  action=improve  ms=6012  code=None", line);
    }

    [Fact]
    public void Values_are_reduced_to_safe_tokens()
    {
        var log = new AppLog(Path.Combine(_dir.FullName, AppLog.FileName));

        // Even if someone passed text by mistake, line breaks and spaces cannot get through.
        log.Write("capture", "a b\nc", null, "Fehler mit\r\nUmbruch und sehr langem Text der abgeschnitten wird");

        var line = Assert.Single(File.ReadAllLines(log.FilePath));
        Assert.Contains("action=abc", line);
        Assert.DoesNotContain(" mit", line);
        Assert.True(line.Length < 120);
    }

    [Fact]
    public void Rotates_when_too_big()
    {
        var log = new AppLog(Path.Combine(_dir.FullName, AppLog.FileName), maxBytes: 200);
        for (var i = 0; i < 10; i++)
            log.Write("start");

        Assert.True(File.Exists(log.RotatedPath));
        Assert.True(new FileInfo(log.FilePath).Length < 200);
    }
}
