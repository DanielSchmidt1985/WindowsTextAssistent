using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudeHotkey.Core.Claude;

/// <summary>
/// Everything needed to start one child process. <paramref name="RawArguments"/>, when set, is
/// used verbatim instead of <paramref name="Arguments"/> (only for the cmd.exe route).
/// <paramref name="RemovedEnvironmentVariables"/> may end in <c>*</c> to remove a whole prefix.
/// </summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? RawArguments,
    string WorkingDirectory,
    string? StandardInput,
    IReadOnlyList<string> RemovedEnvironmentVariables);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Thrown when the executable cannot be started at all (not found, no access).</summary>
public sealed class ProcessStartException(string message, Exception inner) : Exception(message, inner);

/// <summary>Seam for starting processes, so <see cref="ClaudeCli"/> can be tested without a real CLI.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process to completion. When <paramref name="cancellationToken"/> fires, the whole
    /// process tree is killed and <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            WorkingDirectory = request.WorkingDirectory,
        };
        if (request.RawArguments is not null)
            startInfo.Arguments = request.RawArguments;
        else
            foreach (var argument in request.Arguments)
                startInfo.ArgumentList.Add(argument);
        RemoveEnvironmentVariables(startInfo.Environment, request.RemovedEnvironmentVariables);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartException($"Could not start '{request.FileName}': {ex.Message}", ex);
        }

        using var killOnCancel = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            if (request.StandardInput is not null)
                await process.StandardInput.WriteAsync(request.StandardInput).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested || process.HasExited)
        {
            // The process went away while we were writing; the exit handling below reports why.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>Removes the given names; a trailing <c>*</c> removes every variable with that prefix.</summary>
    internal static void RemoveEnvironmentVariables(IDictionary<string, string?> environment, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!name.EndsWith('*'))
            {
                environment.Remove(name);
                continue;
            }
            var prefix = name[..^1];
            foreach (var key in environment.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                environment.Remove(key);
        }
    }
}
