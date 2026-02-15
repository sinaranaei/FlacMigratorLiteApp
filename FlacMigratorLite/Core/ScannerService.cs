using System.Globalization;
using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

namespace FlacMigratorLite.Core;

// Service that scans source directory recursively for .flac files.
// Uses ffprobe to determine duration.
// Returns list of TrackInfo.
public class ScannerService
{
    private readonly ProcessRunner _runner;
    private readonly Logger _logger;

    public ScannerService(ProcessRunner runner, Logger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<List<TrackInfo>> ScanAsync(MigrationConfig config, CancellationToken cancellationToken)
    {
        var tracks = new List<TrackInfo>();
        var files = Directory.EnumerateFiles(config.SourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var duration = await ProbeDurationAsync(config, file, cancellationToken).ConfigureAwait(false);
            var relative = Path.GetRelativePath(config.SourceDirectory, file);
            var target = Path.Combine(config.TargetDirectory, Path.ChangeExtension(relative, ".mp3"));
            var info = new FileInfo(file);
            var mp3Estimate = EstimateMp3Bytes(duration, config.Mp3BitrateKbps);

            tracks.Add(new TrackInfo
            {
                SourcePath = file,
                RelativePath = relative,
                TargetPath = target,
                SizeBytes = info.Length,
                Duration = duration,
                Mp3SizeEstimateBytes = mp3Estimate,
                Status = duration == TimeSpan.Zero ? TrackStatus.Failed : TrackStatus.Pending,
                LastError = duration == TimeSpan.Zero ? "Duration probe failed." : null
            });
        }

        _logger.Info($"Found {tracks.Count} FLAC files.");
        return tracks;
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

    private static long EstimateMp3Bytes(TimeSpan duration, int bitrateKbps)
    {
        var seconds = duration.TotalSeconds;
        var bitsPerSecond = bitrateKbps * 1000.0;
        return (long)(seconds * bitsPerSecond / 8.0);
    }
}
