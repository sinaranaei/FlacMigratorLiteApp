using FlacMigratorLite.Core;
using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

var logger = new Logger();

if (args.Length < 2)
{
	PrintUsage();
	return;
}

var sourceDirectory = Path.GetFullPath(args[0]);
var targetDirectory = Path.GetFullPath(args[1]);
var deleteVerified = args.Any(arg => string.Equals(arg, "--delete", StringComparison.OrdinalIgnoreCase));
var retryFailed = args.Any(arg => string.Equals(arg, "--retry-failed", StringComparison.OrdinalIgnoreCase));

if (!Directory.Exists(sourceDirectory))
{
	logger.Error($"Source directory not found: {sourceDirectory}");
	return;
}

if (sourceDirectory.Equals(targetDirectory, StringComparison.OrdinalIgnoreCase))
{
	logger.Error("Target directory must be different from source.");
	return;
}

if (targetDirectory.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase))
{
	logger.Error("Target directory cannot be inside the source directory.");
	return;
}

Directory.CreateDirectory(targetDirectory);

var config = new MigrationConfig
{
	SourceDirectory = sourceDirectory,
	TargetDirectory = targetDirectory,
	StateFilePath = Path.Combine(targetDirectory, "migration_state.json"),
	DeleteVerified = deleteVerified
};

var runner = new ProcessRunner();
var scanner = new ScannerService(runner, logger);
var estimator = new EstimationService();
var converter = new ConversionService(runner, logger);
var verifier = new VerificationService(runner, logger);
var cleanup = new CleanupService(logger);
var stateStore = new StateStore(config.StateFilePath, logger);

var cancellationToken = CancellationToken.None;

logger.Info("Scanning FLAC files...");
var tracks = await scanner.ScanAsync(config, cancellationToken).ConfigureAwait(false);

var state = stateStore.Load();
var stateIndex = state.Tracks.ToDictionary(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase);

foreach (var track in tracks)
{
	if (stateIndex.TryGetValue(track.SourcePath, out var entry))
	{
		if (entry.Status == TrackStatus.Verified && File.Exists(entry.TargetPath))
		{
			track.Status = TrackStatus.Verified;
		}
		else if (entry.Status == TrackStatus.Converted && File.Exists(entry.TargetPath))
		{
			track.Status = TrackStatus.Converted;
		}
		else if (entry.Status == TrackStatus.Failed)
		{
			if (!retryFailed)
			{
				track.Status = TrackStatus.Failed;
				track.LastError = entry.LastError;
			}
		}
	}
}

if (retryFailed)
{
	logger.Info("Retrying failed tracks from previous run.");
}

var estimation = estimator.Calculate(tracks, null);
PrintSummary(tracks.Count, estimation);

if (!HasEnoughFreeSpace(targetDirectory, estimation.EstimatedMp3SizeBytes + config.SafetyFreeSpaceBufferBytes))
{
	logger.Warn("Free space check indicates possible insufficient space.");
}

Console.Write("Proceed with migration? (y/N): ");
var confirm = Console.ReadLine();
if (!string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
{
	logger.Info("Cancelled by user.");
	return;
}

var benchmarkSamples = new List<double>();

foreach (var track in tracks)
{
	if (track.Status == TrackStatus.Verified || track.Status == TrackStatus.Failed)
	{
		continue;
	}

	if (track.Status != TrackStatus.Converted)
	{
		var conversion = await converter.ConvertAsync(track, config, cancellationToken).ConfigureAwait(false);
		if (!conversion.Success)
		{
			track.Status = TrackStatus.Failed;
			track.LastError = conversion.Error ?? "Conversion failed.";
			UpdateState(stateIndex, track, null);
			stateStore.Save(BuildState(stateIndex));
			logger.Error($"Conversion failed for {track.RelativePath}: {track.LastError}");
			continue;
		}

		track.Status = TrackStatus.Converted;
		if (!conversion.Skipped && track.Duration.TotalSeconds > 0)
		{
			benchmarkSamples.Add(conversion.Elapsed.TotalSeconds / track.Duration.TotalSeconds);
			if (benchmarkSamples.Count == 3)
			{
				var avg = benchmarkSamples.Average();
				var etaEstimate = estimator.Calculate(tracks, avg);
				if (etaEstimate.EstimatedEta.HasValue)
				{
					logger.Info($"ETA based on benchmark: {etaEstimate.EstimatedEta.Value:hh\\:mm\\:ss}");
				}
			}
		}
	}

	var verification = await verifier.VerifyAsync(track, config, cancellationToken).ConfigureAwait(false);
	if (!verification.Success)
	{
		track.Status = TrackStatus.Failed;
		track.LastError = verification.Error;
		UpdateState(stateIndex, track, null);
		stateStore.Save(BuildState(stateIndex));
		logger.Error($"Verification failed for {track.RelativePath}: {track.LastError}");
		continue;
	}

	track.Status = TrackStatus.Verified;
	UpdateState(stateIndex, track, DateTime.UtcNow);
	stateStore.Save(BuildState(stateIndex));
}

if (config.DeleteVerified)
{
	cleanup.DeleteVerified(tracks);
}
else
{
	logger.Info("Deletion disabled. Verified FLAC files were kept.");
}

logger.Info("Migration complete.");

void PrintUsage()
{
	Console.WriteLine("Usage: FlacMigratorLite <sourceDir> <targetDir> [--delete] [--retry-failed]");
}

void PrintSummary(int totalFiles, EstimationResult estimation)
{
	logger.Info($"Files: {totalFiles}");
	logger.Info($"Total size: {FormatBytes(estimation.TotalSizeBytes)}");
	logger.Info($"Total duration: {estimation.TotalDuration:hh\\:mm\\:ss}");
	logger.Info($"Estimated MP3 size: {FormatBytes(estimation.EstimatedMp3SizeBytes)}");
	logger.Info($"Estimated compression: {estimation.EstimatedCompressionRatio:P0}");
}

bool HasEnoughFreeSpace(string targetDir, long requiredBytes)
{
	try
	{
		var root = Path.GetPathRoot(targetDir) ?? targetDir;
		var drive = new DriveInfo(root);
		return drive.AvailableFreeSpace > requiredBytes;
	}
	catch
	{
		return true;
	}
}

void UpdateState(Dictionary<string, TrackStateEntry> index, TrackInfo track, DateTime? verifiedAt)
{
	if (!index.TryGetValue(track.SourcePath, out var entry))
	{
		entry = new TrackStateEntry
		{
			SourcePath = track.SourcePath,
			TargetPath = track.TargetPath
		};
		index[track.SourcePath] = entry;
	}

	entry.Status = track.Status;
	entry.LastError = track.LastError;
	entry.DurationSeconds = track.Duration.TotalSeconds;
	entry.SizeBytes = track.SizeBytes;
	entry.Mp3SizeEstimateBytes = track.Mp3SizeEstimateBytes;
	if (verifiedAt.HasValue)
	{
		entry.VerifiedAtUtc = verifiedAt;
	}
}

MigrationState BuildState(Dictionary<string, TrackStateEntry> index)
{
	return new MigrationState
	{
		Tracks = index.Values.ToList()
	};
}

string FormatBytes(long bytes)
{
	string[] sizes = { "B", "KB", "MB", "GB", "TB" };
	double len = bytes;
	var order = 0;
	while (len >= 1024 && order < sizes.Length - 1)
	{
		order++;
		len /= 1024;
	}

	return $"{len:0.##} {sizes[order]}";
}
