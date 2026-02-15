using System.Diagnostics;

namespace FlacMigratorLite.Infrastructure;

// Utility for running external processes like ffmpeg and ffprobe.
// Must capture stdout, stderr, exit code, and support timeout.
public class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = string.Empty;
        var stderr = string.Empty;

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, "Failed to start process.", false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync(cancellationToken);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        var completed = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask, waitTask), timeoutTask).ConfigureAwait(false);

        if (completed == timeoutTask)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Ignore kill failures on timeout cleanup.
            }

            return new ProcessResult(-1, string.Empty, "Process timed out.", true);
        }

        stdout = await stdoutTask.ConfigureAwait(false);
        stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout, stderr, false);
    }
}

public record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool IsSuccess => !TimedOut && ExitCode == 0;
}
