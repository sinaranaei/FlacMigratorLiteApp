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

    public async Task<List<TrackInfo>> ScanAsync(MigrationConfig config, CancellationToken cancellationToken, Action<int, string>? progressCallback = null)
    {
        var tracks = new List<TrackInfo>();
        var files = Directory.EnumerateFiles(config.SourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));

        var fileList = files.ToList();
        var tracksLock = new object();
        var maxParallelProbes = Math.Max(2, Environment.ProcessorCount / 2); // Use 1/2 of CPU cores for aggressive probing

        // Batch files for more efficient probing (8 files per batch)
        const int FilesPerBatch = 8;
        var batches = fileList
            .Select((file, index) => (file, batch: index / FilesPerBatch))
            .GroupBy(x => x.batch)
            .Select(g => g.Select(x => x.file).ToList())
            .ToList();

        var progressLock = new object();
        var processedCount = 0;

        var probeWorker = new WorkerQueue<List<string>>(
            maxParallelProbes,
            async (fileBatch, ct) =>
            {
                var durations = await BatchProbeDurationAsync(config, fileBatch, ct).ConfigureAwait(false);

                for (var i = 0; i < fileBatch.Count; i++)
                {
                    var file = fileBatch[i];
                    var duration = durations.ContainsKey(file) ? durations[file] : TimeSpan.Zero;
                    var relative = Path.GetRelativePath(config.SourceDirectory, file);
                    var target = Path.Combine(config.TargetDirectory, Path.ChangeExtension(relative, ".mp3"));
                    var info = new FileInfo(file);
                    var mp3Estimate = EstimateMp3Bytes(duration, config.Mp3BitrateKbps);

                    var trackInfo = new TrackInfo
                    {
                        SourcePath = file,
                        RelativePath = relative,
                        TargetPath = target,
                        SizeBytes = info.Length,
                        Duration = duration,
                        Mp3SizeEstimateBytes = mp3Estimate,
                        Status = duration == TimeSpan.Zero ? TrackStatus.Failed : TrackStatus.Pending,
                        LastError = duration == TimeSpan.Zero ? "Duration probe failed." : null
                    };

                    lock (tracksLock)
                    {
                        tracks.Add(trackInfo);
                    }

                    lock (progressLock)
                    {
                        processedCount++;
                        if (processedCount % 10 == 0 || processedCount == fileList.Count)
                        {
                            progressCallback?.Invoke(processedCount, relative);
                        }
                    }
                }
            },
            _logger
        );

        await probeWorker.ProcessAsync(batches, cancellationToken).ConfigureAwait(false);

        return tracks;
    }

    private async Task<Dictionary<string, TimeSpan>> BatchProbeDurationAsync(MigrationConfig config, List<string> filePaths, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, TimeSpan>();

        if (filePaths.Count == 0)
        {
            return result;
        }

        // Build ffprobe command with multiple input files
        var inputArgs = string.Join(" ", filePaths.Select(f => $"\"{f}\""));
        var args = $"-v error -show_entries format=filename,duration -of default=noprint_wrappers=1:nokey=1 {inputArgs}";

        var probeResult = await _runner.RunAsync(config.FfprobePath, args, null, config.FfprobeTimeoutSeconds * 2, cancellationToken).ConfigureAwait(false);

        if (!probeResult.IsSuccess)
        {
            // Fallback to individual probes if batch fails
            foreach (var filePath in filePaths)
            {
                var duration = await ProbeDurationAsync(config, filePath, cancellationToken).ConfigureAwait(false);
                result[filePath] = duration;
            }
            return result;
        }

        // Parse batch output: alternating filename and duration
        var lines = probeResult.StdOut.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length - 1; i += 2)
        {
            var fileLine = lines[i].Trim();
            var durationLine = lines[i + 1].Trim();

            // Match filename to original path (handle path variations)
            var matchedFile = filePaths.FirstOrDefault(f => 
                f.EndsWith(fileLine, StringComparison.OrdinalIgnoreCase) || 
                fileLine.Contains(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase));

            if (matchedFile != null && double.TryParse(durationLine, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                result[matchedFile] = TimeSpan.FromSeconds(seconds);
            }
        }

        // Fallback for any files not found in batch output
        foreach (var filePath in filePaths)
        {
            if (!result.ContainsKey(filePath))
            {
                result[filePath] = await ProbeDurationAsync(config, filePath, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
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
