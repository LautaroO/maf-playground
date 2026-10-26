using System.Diagnostics;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.CLI.Commands;

internal static class StudioSourceRevision
{
    internal static async Task<StudioSourceProvenance> ReadAsync(string directory, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<string?>>? read = null)
    {
        // A published runtime has no Git requirement. Only an explicit source checkout is eligible.
        if (!File.Exists(Path.Combine(directory, "MafPlayground.slnx")) ||
            !(File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git")))) return new();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        read ??= async (arguments, token) =>
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null) return null;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(token);
                var error = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                await error;
                return process.ExitCode == 0 ? (await output).Trim() : null;
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        };
        try
        {
            string? revision = await read("rev-parse --verify HEAD", timeout.Token);
            string? status = await read("status --porcelain --untracked-files=normal", timeout.Token);
            return new(revision, status is null ? null : status.Length > 0);
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new();
        }
    }
}
