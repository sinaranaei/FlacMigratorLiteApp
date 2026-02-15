namespace FlacMigratorLite.Models;

// Represents configuration used to run a migration session.
public class MigrationConfig
{
    public string SourceDirectory { get; init; } = string.Empty;
    public string TargetDirectory { get; init; } = string.Empty;
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string FfprobePath { get; init; } = "ffprobe";
    public string StateFilePath { get; init; } = "migration_state.json";
    public bool DeleteVerified { get; init; } = true;
    public int Mp3BitrateKbps { get; init; } = 320;
    public int DurationToleranceSeconds { get; init; } = 1;
    public int FfmpegTimeoutSeconds { get; init; } = 1800;
    public int FfprobeTimeoutSeconds { get; init; } = 30;
    public long SafetyFreeSpaceBufferBytes { get; init; } = 500L * 1024L * 1024L;
}
