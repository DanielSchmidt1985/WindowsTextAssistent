using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ClaudeHotkey.Core.Update;

namespace ClaudeHotkey.Tests;

public class UpdaterTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("claudehotkey-update");
    private static readonly byte[] NewExe = Encoding.UTF8.GetBytes("new exe content");

    public void Dispose() => _dir.Delete(recursive: true);

    private string ExePath => Path.Combine(_dir.FullName, "ClaudeHotkey.exe");

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // Shape taken from the real answer of /releases/latest for v1.1.1.
    private static string ReleaseJson(string tag = "v1.2.0", string? digest = null, long? size = null, string name = "ClaudeHotkey.exe",
                                      bool prerelease = false) => $$"""
        {"tag_name":"{{tag}}","name":"{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},
         "assets":[{"name":"{{name}}","size":{{size ?? NewExe.Length}},
                    "digest":"{{digest ?? "sha256:" + Sha(NewExe)}}",
                    "browser_download_url":"https://github.com/DanielSchmidt1985/WindowsTextAssistent/releases/download/{{tag}}/{{name}}"}]}
        """;

    private sealed class FakeTransport(string json, byte[] download) : IUpdateTransport
    {
        public Exception? Failure { get; init; }
        public List<string> Downloads { get; } = [];

        public Task<string> GetLatestReleaseJsonAsync(CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(json) : Task.FromException<string>(Failure);

        public async Task DownloadAsync(string url, Stream destination, CancellationToken cancellationToken)
        {
            Downloads.Add(url);
            await destination.WriteAsync(download, cancellationToken);
        }
    }

    private Updater Create(FakeTransport transport, string version = "1.1.1.0") => new(transport, ExePath, Version.Parse(version));

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.10.3", "1.10.3")]
    [InlineData("v2.0", "2.0.0")]
    [InlineData("latest", null)]
    [InlineData("", null)]
    public void Tag_is_read_as_version(string tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), Updater.ParseTag(tag));

    [Fact]
    public void Release_json_gives_asset_url_size_and_digest()
    {
        var release = Updater.ParseRelease(ReleaseJson())!;

        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("https://github.com/DanielSchmidt1985/WindowsTextAssistent/releases/download/v1.2.0/ClaudeHotkey.exe", release.AssetUrl);
        Assert.Equal(NewExe.Length, release.Size);
        Assert.Equal(Sha(NewExe), release.Sha256);
    }

    [Fact]
    public void Release_without_exe_or_marked_prerelease_is_not_used()
    {
        Assert.Null(Updater.ParseRelease(ReleaseJson(name: "Other.zip")));
        Assert.Null(Updater.ParseRelease(ReleaseJson(prerelease: true)));
        Assert.Null(Updater.ParseRelease("not json"));
        Assert.Null(Updater.ParseRelease(ReleaseJson().Replace("https://github.com/", "https://example.com/")));
    }

    [Theory]
    [InlineData("1.1.1.0", true)]
    [InlineData("1.2.0.0", false)]
    [InlineData("1.3.0.0", false)]
    public async Task Check_reports_only_newer_releases(string current, bool expectUpdate)
    {
        var (outcome, release) = await Create(new FakeTransport(ReleaseJson(), NewExe), current).CheckAsync(default);

        Assert.Equal(expectUpdate ? UpdateOutcome.Ready : UpdateOutcome.UpToDate, outcome);
        Assert.Equal(expectUpdate, release is not null);
    }

    [Fact]
    public async Task Check_without_network_is_a_quiet_outcome()
    {
        var transport = new FakeTransport("", NewExe) { Failure = new HttpRequestException("offline") };

        var (outcome, _) = await Create(transport).CheckAsync(default);

        Assert.Equal(UpdateOutcome.NoConnection, outcome);
    }

    [Fact]
    public async Task Download_is_verified_and_swapped_in()
    {
        File.WriteAllText(ExePath, "old exe");
        var updater = Create(new FakeTransport(ReleaseJson(), NewExe));
        var (_, release) = await updater.CheckAsync(default);

        Assert.Equal(UpdateOutcome.Ready, await updater.DownloadAsync(release!, default));
        Assert.Equal(UpdateOutcome.Ready, Updater.Swap(ExePath));

        Assert.Equal(NewExe, File.ReadAllBytes(ExePath));
        Assert.Equal("old exe", File.ReadAllText(Updater.OldPath(ExePath)));
        Assert.False(File.Exists(updater.NewPath));

        Updater.CleanUp(ExePath);
        Assert.False(File.Exists(Updater.OldPath(ExePath)));
    }

    [Fact]
    public async Task Wrong_checksum_deletes_the_download_and_keeps_the_exe()
    {
        File.WriteAllText(ExePath, "old exe");
        var updater = Create(new FakeTransport(ReleaseJson(digest: "sha256:" + new string('0', 64)), NewExe));
        var (_, release) = await updater.CheckAsync(default);

        Assert.Equal(UpdateOutcome.ChecksumMismatch, await updater.DownloadAsync(release!, default));

        Assert.False(File.Exists(updater.NewPath));
        Assert.Equal("old exe", File.ReadAllText(ExePath));
    }

    [Fact]
    public async Task Wrong_size_is_rejected_even_without_digest()
    {
        var json = ReleaseJson(size: NewExe.Length + 1).Replace("\"digest\":\"sha256:" + Sha(NewExe) + "\",", "");
        var updater = Create(new FakeTransport(json, NewExe));
        var (_, release) = await updater.CheckAsync(default);
        Assert.Null(release!.Sha256);

        Assert.Equal(UpdateOutcome.ChecksumMismatch, await updater.DownloadAsync(release, default));
    }

    [Fact]
    public void Swap_restores_the_exe_when_the_new_file_is_missing()
    {
        File.WriteAllText(ExePath, "old exe");
        var updater = Create(new FakeTransport(ReleaseJson(), NewExe));

        Assert.Equal(UpdateOutcome.WriteFailed, Updater.Swap(ExePath));

        Assert.Equal("old exe", File.ReadAllText(ExePath));
    }

    [Fact]
    public void Only_the_installed_copy_updates_itself()
    {
        var local = @"C:\Users\x\AppData\Local";

        Assert.True(Updater.IsInstalledCopy(@"C:\Users\x\AppData\Local\Programs\ClaudeHotkey\ClaudeHotkey.exe", local));
        Assert.True(Updater.IsInstalledCopy(@"c:\users\X\appdata\local\programs\claudehotkey\claudehotkey.EXE", local));
        Assert.False(Updater.IsInstalledCopy(@"C:\src\ClaudeHotkey\bin\Debug\net8.0-windows\ClaudeHotkey.exe", local));
    }
}
