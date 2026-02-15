namespace FlacMigratorLite.Models;

// Represents aggregated size, duration, and ETA estimates.
public class EstimationResult
{
    public long TotalSizeBytes { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public long EstimatedMp3SizeBytes { get; init; }
    public TimeSpan? EstimatedEta { get; init; }
    public double EstimatedCompressionRatio { get; init; }
}
