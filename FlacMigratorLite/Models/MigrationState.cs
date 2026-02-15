namespace FlacMigratorLite.Models;

// Represents persisted migration progress so the job can resume safely.
public class MigrationState
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<TrackStateEntry> Tracks { get; set; } = new();
}

public class TrackStateEntry
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public TrackStatus Status { get; set; } = TrackStatus.Pending;
    public string? LastError { get; set; }
    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }
    public long Mp3SizeEstimateBytes { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}
