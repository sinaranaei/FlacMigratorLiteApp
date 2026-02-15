namespace FlacMigratorLite.Models;

// Represents a FLAC track discovered in the archive.
// Contains path, duration, file size, and status of migration.
public class TrackInfo
{
    public string SourcePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public long Mp3SizeEstimateBytes { get; init; }
    public TrackStatus Status { get; set; } = TrackStatus.Pending;
    public string? LastError { get; set; }
}

public enum TrackStatus
{
    Pending = 0,
    Converted = 1,
    Verified = 2,
    Failed = 3
}
