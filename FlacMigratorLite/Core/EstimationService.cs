using FlacMigratorLite.Models;

namespace FlacMigratorLite.Core;

// Calculates total archive size, total duration,
// estimated MP3 size at 320kbps, and estimated conversion time.
public class EstimationService
{
    public EstimationResult Calculate(IEnumerable<TrackInfo> tracks, double? secondsPerSecond)
    {
        var trackList = tracks.ToList();
        var totalSize = trackList.Sum(t => t.SizeBytes);
        var totalDurationSeconds = trackList.Sum(t => t.Duration.TotalSeconds);
        var totalDuration = TimeSpan.FromSeconds(totalDurationSeconds);
        var mp3Estimate = trackList.Sum(t => t.Mp3SizeEstimateBytes);
        var ratio = totalSize > 0 ? (double)mp3Estimate / totalSize : 0;

        TimeSpan? eta = null;
        if (secondsPerSecond.HasValue && secondsPerSecond.Value > 0)
        {
            eta = TimeSpan.FromSeconds(totalDurationSeconds * secondsPerSecond.Value);
        }

        return new EstimationResult
        {
            TotalSizeBytes = totalSize,
            TotalDuration = totalDuration,
            EstimatedMp3SizeBytes = mp3Estimate,
            EstimatedEta = eta,
            EstimatedCompressionRatio = ratio
        };
    }
}
