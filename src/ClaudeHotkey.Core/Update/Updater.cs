using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClaudeHotkey.Core.Update;

/// <summary>The newest release on GitHub and its exe asset.</summary>
/// <param name="Sha256">Lower-case hex from the asset's "digest" ("sha256:…"); null when GitHub gave none.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string AssetUrl, long Size, string? Sha256);

public enum UpdateOutcome
{
    UpToDate,
    /// <summary>The new exe is downloaded and verified, waiting to be swapped in.</summary>
    Ready,
    /// <summary>GitHub could not be reached or answered with an error.</summary>
    NoConnection,
    /// <summary>The release has no usable exe asset or an unreadable tag.</summary>
    BadRelease,
    /// <summary>The download did not match size or SHA-256.</summary>
    ChecksumMismatch,
    /// <summary>The new file could not be written or moved into place.</summary>
    WriteFailed,
}

/// <summary>Network access of the updater; the test seam.</summary>
public interface IUpdateTransport
{
    Task<string> GetLatestReleaseJsonAsync(CancellationToken cancellationToken);
    Task DownloadAsync(string url, Stream destination, CancellationToken cancellationToken);
}

/// <summary>GitHub over HTTPS. GitHub's API refuses requests without a User-Agent.</summary>
public sealed class GitHubTransport : IUpdateTransport, IDisposable
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/DanielSchmidt1985/WindowsTextAssistent/releases/latest";

    private readonly HttpClient _http;

    public GitHubTransport(Version currentVersion)
    {
        // No overall timeout: the exe is about 160 MB. Cancellation comes from the caller.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeHotkey/{currentVersion.ToString(3)}");
    }

    public async Task<string> GetLatestReleaseJsonAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    public async Task DownloadAsync(string url, Stream destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Self-update of the installed exe from the GitHub releases: check, download next to the exe as
/// "<exe>.new", verify, then swap. A running exe cannot be overwritten on Windows but can be
/// renamed, so the swap renames it to "<exe>.old" and moves the new file into its place; the new
/// instance deletes the old file on its next start (<see cref="CleanUp"/>).
/// </summary>
public sealed class Updater(IUpdateTransport transport, string exePath, Version currentVersion)
{
    public const string AssetName = "ClaudeHotkey.exe";

    public string NewPath => NewPathOf(exePath);
    public static string NewPathOf(string exePath) => exePath + ".new";
    public static string OldPath(string exePath) => exePath + ".old";

    /// <summary>Only the copy under %LOCALAPPDATA%\Programs\ClaudeHotkey updates itself, never a build in the repo.</summary>
    public static bool IsInstalledCopy(string exePath, string localAppData) =>
        string.Equals(Path.GetFullPath(exePath),
                      Path.GetFullPath(Path.Combine(localAppData, "Programs", "ClaudeHotkey", AssetName)),
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>"v1.2.0" → 1.2.0; null for anything else.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>Major.minor.build only, so 1.1.1 and the assembly's 1.1.1.0 compare equal.</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    /// <summary>Reads the answer of /releases/latest; null when it has no usable exe asset.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease"))
                return null;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (ParseTag(tag) is not { } version || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && name.GetString() == AssetName
                    && asset.TryGetProperty("browser_download_url", out var url) && url.GetString() is { } link
                    && link.StartsWith("https://github.com/", StringComparison.Ordinal)
                    && asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) && bytes > 0)
                {
                    var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                    var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                        ? digest["sha256:".Length..].ToLowerInvariant()
                        : null;
                    return new ReleaseInfo(version, tag!, link, bytes, sha);
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Asks GitHub for the newest release; Release is set when it is newer than this version.</summary>
    public async Task<(UpdateOutcome Outcome, ReleaseInfo? Release)> CheckAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await transport.GetLatestReleaseJsonAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (UpdateOutcome.NoConnection, null);
        }
        if (ParseRelease(json) is not { } release)
            return (UpdateOutcome.BadRelease, null);
        return release.Version > Normalize(currentVersion) ? (UpdateOutcome.Ready, release) : (UpdateOutcome.UpToDate, null);
    }

    /// <summary>Downloads to <see cref="NewPath"/> and checks size and SHA-256; a bad file is deleted.</summary>
    public async Task<UpdateOutcome> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        try
        {
            await using (var file = new FileStream(NewPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await transport.DownloadAsync(release.AssetUrl, file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            TryDelete(NewPath);
            return UpdateOutcome.NoConnection;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(NewPath);
            return UpdateOutcome.WriteFailed;
        }
        catch
        {
            TryDelete(NewPath);
            throw;
        }

        if (!await MatchesAsync(NewPath, release, cancellationToken).ConfigureAwait(false))
        {
            TryDelete(NewPath);
            return UpdateOutcome.ChecksumMismatch;
        }
        return UpdateOutcome.Ready;
    }

    private static async Task<bool> MatchesAsync(string path, ReleaseInfo release, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != release.Size)
            return false;
        if (release.Sha256 is null)
            return true;
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Puts the downloaded file in place of the running exe; on failure the old exe is restored.</summary>
    public static UpdateOutcome Swap(string exePath)
    {
        var old = OldPath(exePath);
        try
        {
            TryDelete(old);
            File.Move(exePath, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateOutcome.WriteFailed;
        }
        try
        {
            File.Move(NewPathOf(exePath), exePath);
            return UpdateOutcome.Ready;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Move(old, exePath); } catch (Exception restore) when (restore is IOException or UnauthorizedAccessException) { }
            return UpdateOutcome.WriteFailed;
        }
    }

    /// <summary>Removes what an update left next to the exe: the old exe and an unfinished download.</summary>
    public static void CleanUp(string exePath)
    {
        TryDelete(OldPath(exePath));
        TryDelete(NewPathOf(exePath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use (the old process has not exited yet); the next start tries again.
        }
    }
}
