using System.Globalization;
using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

namespace FlacMigratorLite.Core;

// Verifies MP3 integrity by:
// 1. Comparing duration with original
// 2. Running ffmpeg decode test
public class VerificationService
{
    private readonly ProcessRunner _runner;
    private readonly Logger _logger;

    public VerificationService(ProcessRunner runner, Logger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<VerificationResult> VerifyAsync(TrackInfo track, MigrationConfig config, CancellationToken cancellationToken)
    {
        if (!File.Exists(track.TargetPath))
        {
            return new VerificationResult(false, "MP3 file is missing.");
        }

        if (track.Duration == TimeSpan.Zero)
        {
            return new VerificationResult(false, "Original duration missing.");
        }

        var duration = await ProbeDurationAsync(config, track.TargetPath, cancellationToken).ConfigureAwait(false);
        if (duration == TimeSpan.Zero)
        {
            return new VerificationResult(false, "MP3 duration probe failed.");
        }

        var diff = Math.Abs((duration - track.Duration).TotalSeconds);
        if (diff > config.DurationToleranceSeconds)
        {
            return new VerificationResult(false, $"Duration mismatch ({diff:F1}s)." );
        }

        var decodeArgs = $"-v error -i \"{track.TargetPath}\" -f null -";
        var decodeResult = await _runner.RunAsync(config.FfmpegPath, decodeArgs, null, config.FfmpegTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        if (!decodeResult.IsSuccess)
        {
            return new VerificationResult(false, decodeResult.StdErr.Trim());
        }

        _logger.Success($"Verified {track.RelativePath}");
        return new VerificationResult(true, null);
    }

    private async Task<TimeSpan> ProbeDurationAsync(MigrationConfig config, string filePath, CancellationToken cancellationToken)
    {
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";
        var result = await _runner.RunAsync(config.FfprobePath, args, null, config.FfprobeTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.Warn($"ffprobe failed for {filePath}. {result.StdErr.Trim()}");
            return TimeSpan.Zero;
        }

        if (double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        _logger.Warn($"Unable to parse duration for {filePath}.");
        return TimeSpan.Zero;
    }
}

public record VerificationResult(bool Success, string? Error);
