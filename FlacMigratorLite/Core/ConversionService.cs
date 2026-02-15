using System.Diagnostics;
using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

namespace FlacMigratorLite.Core;

// Converts a single FLAC file to MP3 using ffmpeg.
// Must preserve metadata and mirror folder structure.
// Must not overwrite existing files.
public class ConversionService
{
    private readonly ProcessRunner _runner;
    private readonly Logger _logger;

    public ConversionService(ProcessRunner runner, Logger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<ConversionResult> ConvertAsync(TrackInfo track, MigrationConfig config, CancellationToken cancellationToken)
    {
        if (File.Exists(track.TargetPath))
        {
            return new ConversionResult(true, true, null, TimeSpan.Zero);
        }

        var targetDir = Path.GetDirectoryName(track.TargetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var tempPath = track.TargetPath + ".part";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var args = string.Join(' ', new[]
        {
            "-y",
            $"-i \"{track.SourcePath}\"",
            "-map 0",
            "-map_metadata 0",
            "-c:a libmp3lame",
            $"-b:a {config.Mp3BitrateKbps}k",
            "-c:v copy",
            "-id3v2_version 3",
            "-write_id3v1 1",
            $"\"{tempPath}\""
        });

        var stopwatch = Stopwatch.StartNew();
        var result = await _runner.RunAsync(config.FfmpegPath, args, null, config.FfmpegTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (!result.IsSuccess)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            return new ConversionResult(false, false, result.StdErr.Trim(), stopwatch.Elapsed);
        }

        if (File.Exists(track.TargetPath))
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            return new ConversionResult(false, false, "Target file already exists.", stopwatch.Elapsed);
        }

        File.Move(tempPath, track.TargetPath, false);
        _logger.Success($"Converted {track.RelativePath}");
        return new ConversionResult(true, false, null, stopwatch.Elapsed);
    }
}

public record ConversionResult(bool Success, bool Skipped, string? Error, TimeSpan Elapsed);
