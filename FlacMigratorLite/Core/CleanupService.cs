using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

namespace FlacMigratorLite.Core;

// Deletes verified FLAC files after full migration pass.
public class CleanupService
{
    private readonly Logger _logger;

    public CleanupService(Logger logger)
    {
        _logger = logger;
    }

    public void DeleteVerified(IEnumerable<TrackInfo> tracks)
    {
        foreach (var track in tracks.Where(t => t.Status == TrackStatus.Verified))
        {
            if (File.Exists(track.SourcePath))
            {
                File.Delete(track.SourcePath);
                _logger.Info($"Deleted {track.RelativePath}");
            }
        }
    }
}
