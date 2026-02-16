using FlacMigratorLite.Core;
using FlacMigratorLite.Infrastructure;
using FlacMigratorLite.Models;

var logger = new Logger();
var statusDisplay = new StatusDisplay(logger);

var normalizedArgs = args.Where(arg => !string.Equals(arg, "--", StringComparison.Ordinal)).ToArray();
var inPlace = normalizedArgs.Any(arg => string.Equals(arg, "--in-place", StringComparison.OrdinalIgnoreCase));
var deleteVerified = normalizedArgs.Any(arg => string.Equals(arg, "--delete", StringComparison.OrdinalIgnoreCase));
var retryFailed = normalizedArgs.Any(arg => string.Equals(arg, "--retry-failed", StringComparison.OrdinalIgnoreCase));
var fullVerify = normalizedArgs.Any(arg => string.Equals(arg, "--full-verify", StringComparison.OrdinalIgnoreCase));
var extremeMode = normalizedArgs.Any(arg => string.Equals(arg, "--extreme", StringComparison.OrdinalIgnoreCase));
int? convertWorkersOverride = null;
int? ffmpegThreadsOverride = null;

// Parse named flags
string? sourceDirectory = null;
string? targetDirectory = null;

for (int i = 0; i < normalizedArgs.Length; i++)
{
	if (string.Equals(normalizedArgs[i], "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < normalizedArgs.Length)
	{
		sourceDirectory = normalizedArgs[++i];
	}
	else if (string.Equals(normalizedArgs[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < normalizedArgs.Length)
	{
		targetDirectory = normalizedArgs[++i];
	}
	else if (string.Equals(normalizedArgs[i], "--convert-workers", StringComparison.OrdinalIgnoreCase) && i + 1 < normalizedArgs.Length)
	{
		if (int.TryParse(normalizedArgs[++i], out var value) && value > 0)
		{
			convertWorkersOverride = value;
		}
		else
		{
			logger.Error("--convert-workers must be a positive integer.");
			return;
		}
	}
	else if (string.Equals(normalizedArgs[i], "--ffmpeg-threads", StringComparison.OrdinalIgnoreCase) && i + 1 < normalizedArgs.Length)
	{
		if (int.TryParse(normalizedArgs[++i], out var value) && value > 0)
		{
			ffmpegThreadsOverride = value;
		}
		else
		{
			logger.Error("--ffmpeg-threads must be a positive integer.");
			return;
		}
	}
}

// Fall back to positional args if named flags not provided
var positionalArgs = normalizedArgs
	.Where(arg => !arg.StartsWith("--"))
	.Where(arg => !arg.StartsWith("-"))
	.ToArray();

if (string.IsNullOrEmpty(sourceDirectory))
{
	if (positionalArgs.Length < 1)
	{
		PrintUsage();
		return;
	}
	sourceDirectory = positionalArgs[0];
}

if (string.IsNullOrEmpty(targetDirectory) && !inPlace)
{
	if (positionalArgs.Length < 2)
	{
		PrintUsage();
		return;
	}
	targetDirectory = positionalArgs[1];
}

sourceDirectory = Path.GetFullPath(sourceDirectory);
if (inPlace)
{
	targetDirectory = sourceDirectory;
}
else if (!string.IsNullOrEmpty(targetDirectory))
{
	targetDirectory = Path.GetFullPath(targetDirectory);
}

if (!Directory.Exists(sourceDirectory))
{
	logger.Error($"Source directory not found: {sourceDirectory}");
	return;
}

if (!inPlace)
{
	if (string.IsNullOrEmpty(targetDirectory))
	{
		logger.Error("Target directory is required when not in in-place mode.");
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
}

if (string.IsNullOrEmpty(targetDirectory))
{
	logger.Error("Target directory path is null or empty.");
	return;
}

Directory.CreateDirectory(targetDirectory);

// Determine concurrency level based on CPU cores - quality-first with optimization
var processorCount = Environment.ProcessorCount;
var maxConcurrentConverts = Math.Max(2, (int)(processorCount * 0.75)); // 75% of cores for conversion
var maxConcurrentVerifies = Math.Max(2, processorCount); // Full cores for verification (lighter I/O)
var scanWorkers = Math.Max(2, processorCount / 2);
if (extremeMode)
{
	maxConcurrentConverts = processorCount;
	maxConcurrentVerifies = processorCount;
	scanWorkers = processorCount;
}
if (convertWorkersOverride.HasValue)
{
	maxConcurrentConverts = convertWorkersOverride.Value;
}

var config = new MigrationConfig
{
	SourceDirectory = sourceDirectory,
	TargetDirectory = targetDirectory,
	StateFilePath = Path.Combine(targetDirectory, "migration_state.json"),
	DeleteVerified = deleteVerified,
	FfmpegThreads = ffmpegThreadsOverride ?? 0,
	ScanWorkers = scanWorkers
};

var runner = new ProcessRunner();
var scanner = new ScannerService(runner, logger);
var estimator = new EstimationService();
var converter = new ConversionService(runner, logger);
var verifier = new VerificationService(runner, logger);
var cleanup = new CleanupService(logger);
var stateStore = new AtomicStateStore(config.StateFilePath, logger);
var errorReporter = new ErrorReporter(
    Path.Combine(targetDirectory, "migration_errors.log"),
    logger
);
var cancellationToken = CancellationToken.None;
logger.Info($"Using {scanWorkers} scan workers, {maxConcurrentConverts} conversion workers, and {maxConcurrentVerifies} verification workers.");
if (extremeMode)
{
	logger.Warn("Extreme mode enabled: using all CPU cores for scan, conversion, and verification.");
}
if (ffmpegThreadsOverride.HasValue)
{
	logger.Info($"Using {ffmpegThreadsOverride.Value} ffmpeg threads per conversion.");
}

logger.Info("Scanning FLAC files...");
var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
var scannedCount = 0;
var tracks = await scanner.ScanAsync(config, cancellationToken, (count, path) =>
{
	scannedCount = count;
	statusDisplay.UpdateStatus("Scanning", count, count, path);
	statusDisplay.Render();
}).ConfigureAwait(false);
scanStopwatch.Stop();
statusDisplay.Clear();

logger.Info($"Scan completed in {scanStopwatch.Elapsed:hh\\:mm\\:ss} - Found {tracks.Count} files.");

var state = await stateStore.LoadAsync().ConfigureAwait(false);
var stateIndex = state.Tracks.ToDictionary(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase);
var stateIndexLock = new object();

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

if (inPlace)
{
	logger.Warn("In-place mode enabled: MP3s will be written next to FLAC files.");
}

if (fullVerify)
{
	logger.Warn("Full verification enabled: Each MP3 will be decoded to verify integrity (slower).");
}

var estimation = estimator.Calculate(tracks, null);
PrintSummary(tracks.Count, estimation, fullVerify);

if (!HasEnoughFreeSpace(targetDirectory, estimation.EstimatedMp3SizeBytes + config.SafetyFreeSpaceBufferBytes))
{
	logger.Warn("⚠ Free space check indicates possible insufficient space - proceed with caution!");
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("Start migration? (y/N): ");
Console.ResetColor();
var confirm = Console.ReadLine();
if (!string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
{
	logger.Info("Migration cancelled by user.");
	return;
}

var benchmarkSamples = new List<double>();
var benchmarkLock = new object();
var tracksToConvert = tracks.Where(t => t.Status != TrackStatus.Verified && t.Status != TrackStatus.Failed).ToList();
var tracksToVerify = new List<TrackInfo>();
var tracksToVerifyLock = new object();

logger.Info($"Starting conversion of {tracksToConvert.Count} FLAC files...");

// Phase 1: Parallel conversion
await ConvertInParallelAsync(tracksToConvert, tracksToVerifyLock, tracksToVerify, stateIndexLock, stateIndex, benchmarkLock, benchmarkSamples, statusDisplay).ConfigureAwait(false);
statusDisplay.Clear();

logger.Info($"Conversion complete. Starting verification of {tracksToVerify.Count} files...");

// Phase 2: Parallel verification
await VerifyInParallelAsync(tracksToVerify, stateIndexLock, stateIndex, statusDisplay, fullVerify).ConfigureAwait(false);
statusDisplay.Clear();

if (config.DeleteVerified)
{
	cleanup.DeleteVerified(tracks);
}
else
{
	logger.Info("Deletion disabled. Verified FLAC files were kept.");
}

var finalStats = tracks.GroupBy(t => t.Status).ToDictionary(g => g.Key, g => g.Count());
var verified = finalStats.GetValueOrDefault(TrackStatus.Verified, 0);
var failed = finalStats.GetValueOrDefault(TrackStatus.Failed, 0);

Console.WriteLine();
Console.WriteLine(new string('=', 80));
logger.Success("MIGRATION COMPLETE!");
Console.WriteLine(new string('=', 80));
logger.Info($"  Verified: {verified,4}");
logger.Info($"  Converted (pending verify): {finalStats.GetValueOrDefault(TrackStatus.Converted, 0),4}");
logger.Info($"  Failed: {failed,4}");
logger.Info($"  Pending: {finalStats.GetValueOrDefault(TrackStatus.Pending, 0),4}");
Console.WriteLine(new string('=', 80));

if (failed > 0)
{
	Console.WriteLine();
	logger.Warn($"Failed tracks ({failed}):");
	Console.WriteLine();
	var failedTracks = tracks.Where(t => t.Status == TrackStatus.Failed).OrderBy(t => t.RelativePath).ToList();
	foreach (var track in failedTracks)
	{
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine($"  X {track.RelativePath}");
		Console.ResetColor();
		Console.WriteLine($"    Reason: {track.LastError}");
	}
	Console.WriteLine();
	
	await errorReporter.GenerateReportAsync().ConfigureAwait(false);
	logger.Info("See migration_errors.log for detailed error information.");
}
else
{
	logger.Success("No errors - all tracks migrated successfully!");
}

void PrintUsage()
{
	Console.WriteLine("Usage:");
	Console.WriteLine("  FlacMigratorLite <sourceDir> [<targetDir>] [options]");
	Console.WriteLine("  FlacMigratorLite --source <sourceDir> [--target <targetDir>] [options]");
	Console.WriteLine();
	Console.WriteLine("Options:");
	Console.WriteLine("  --in-place        Write MP3s next to FLACs and delete after verification");
	Console.WriteLine("  --delete          Delete verified FLAC files (default: keep them)");
	Console.WriteLine("  --retry-failed    Retry tracks that failed in a previous run");
	Console.WriteLine("  --full-verify     Decode entire MP3 to verify integrity (much slower)");
	Console.WriteLine("  --extreme         Use all CPU cores for scan/convert/verify");
	Console.WriteLine("  --convert-workers <n>  Override conversion worker count");
	Console.WriteLine("  --ffmpeg-threads <n>   Threads per ffmpeg process (1-4 typical)");
}

void PrintSummary(int totalFiles, EstimationResult estimation, bool fullVerify = false)
{
	// Estimate conversion time: ~0.8-1.0x speed for 320kbps MP3 (depends on CPU)
	var estimatedConversionMultiplier = 1.0; // Conservative: assume ~1:1 real-time
	var estimatedConversionTime = TimeSpan.FromSeconds(estimation.TotalDuration.TotalSeconds * estimatedConversionMultiplier);
	
	// Verify time depends on mode
	TimeSpan estimatedVerifyTime;
	string verifyMode;
	
	if (fullVerify)
	{
		// Full decode test: playing through entire audio = approximately same duration
		estimatedVerifyTime = estimation.TotalDuration;
		verifyMode = "full decode (integrity check)";
	}
	else
	{
		// Fast duration-only: just ffprobe checks, very fast (~15-30 seconds for all)
		estimatedVerifyTime = TimeSpan.FromSeconds(Math.Min(30, totalFiles / 100.0));
		verifyMode = "duration check only";
	}
	
	// Total
	var totalEstimatedTime = TimeSpan.FromSeconds(estimatedConversionTime.TotalSeconds + estimatedVerifyTime.TotalSeconds);
	
	Console.WriteLine();
	Console.WriteLine(new string('-', 80));
	logger.Info("Migration Estimate:");
	Console.WriteLine(new string('-', 80));
	logger.Info($"  Total files: {totalFiles}");
	logger.Info($"  Total size: {FormatBytes(estimation.TotalSizeBytes)}");
	logger.Info($"  Total duration: {estimation.TotalDuration:hh\\:mm\\:ss}");
	logger.Info($"  Estimated MP3 size: {FormatBytes(estimation.EstimatedMp3SizeBytes)}");
	logger.Info($"  Estimated compression: {estimation.EstimatedCompressionRatio:P0}");
	Console.WriteLine(new string('-', 80));
	logger.Info("Processing Timeline:");
	Console.WriteLine(new string('-', 80));
	logger.Info($"  Conversion: ~{estimatedConversionTime:hh\\:mm\\:ss} (parallel processing)");
	logger.Info($"  Verification: ~{estimatedVerifyTime:hh\\:mm\\:ss} ({verifyMode})");
	logger.Info($"  Total time: ~{totalEstimatedTime:hh\\:mm\\:ss}");
	Console.WriteLine(new string('-', 80));
}

async Task ConvertInParallelAsync(List<TrackInfo> tracksToConvert, object verifyLock, List<TrackInfo> tracksToVerify, object stateLock, Dictionary<string, TrackStateEntry> stateIndex, object benchLock, List<double> benchmarkSamples, StatusDisplay statusDisplay)
{
	var convertedCount = 0;
	var convertLock = new object();
	const int BatchSaveInterval = 20; // Save state every 20 conversions to reduce I/O

	var convertWorker = new WorkerQueue<TrackInfo>(
		maxConcurrentConverts,
		async (track, ct) =>
		{
			if (track.Status == TrackStatus.Converted)
			{
				return;
			}

			var conversion = await converter.ConvertAsync(track, config, ct).ConfigureAwait(false);
			if (!conversion.Success)
			{
				track.Status = TrackStatus.Failed;
				track.LastError = conversion.Error ?? "Conversion failed.";
				errorReporter.RecordError(track.SourcePath, track.RelativePath, track.LastError ?? "Conversion failed.", TrackStatus.Failed);
				lock (stateLock)
				{
					UpdateState(stateIndex, track, null);
				}
				lock (convertLock)
				{
					convertedCount++;
					if (convertedCount % BatchSaveInterval == 0)
					{
						// Only save state periodically
					}
				}
				logger.Error($"Conversion failed for {track.RelativePath}");
				logger.Error($"  Reason: {track.LastError}");
				return;
			}

			track.Status = TrackStatus.Converted;
			if (!conversion.Skipped && track.Duration.TotalSeconds > 0)
			{
				lock (benchLock)
				{
					benchmarkSamples.Add(conversion.Elapsed.TotalSeconds / track.Duration.TotalSeconds);
					if (benchmarkSamples.Count == 3)
					{
						var avg = benchmarkSamples.Average();
						var etaEstimate = estimator.Calculate(tracks, avg);
						if (etaEstimate.EstimatedEta.HasValue)
						{
							logger.Progress($"ETA based on benchmark: {etaEstimate.EstimatedEta.Value:hh\\:mm\\:ss}");
						}
					}
				}
			}

			lock (verifyLock)
			{
				tracksToVerify.Add(track);
			}

			lock (convertLock)
			{
				convertedCount++;
				// Save state periodically instead of after every file
				if (convertedCount % BatchSaveInterval == 0)
				{
					lock (stateLock)
					{
						// State will be saved in batch after conversion completes
					}
				}
			}
		},
		logger
	);

	// Start background task to display progress
	var displayTask = Task.Run(async () =>
	{
		while (true)
		{
			var (processed, total) = convertWorker.GetProgress();
			if (total > 0 && processed < total)
			{
				var currentTrack = tracksToConvert.FirstOrDefault(t => t.Status == TrackStatus.Pending);
				var jobName = currentTrack?.RelativePath ?? "idle";
				statusDisplay.UpdateStatus("Converting", processed, total, jobName);
				statusDisplay.Render();
			}

			if (processed >= total && total > 0)
			{
				break;
			}

			await Task.Delay(500).ConfigureAwait(false);
		}
	});

	await convertWorker.ProcessAsync(tracksToConvert, cancellationToken).ConfigureAwait(false);
	await displayTask.ConfigureAwait(false);
	
	// Save state after conversion batch completes
	lock (stateLock)
	{
		// Final state save after all conversions
	}
	await stateStore.SaveAsync(BuildState(stateIndex)).ConfigureAwait(false);
}

async Task VerifyInParallelAsync(List<TrackInfo> tracksToVerify, object stateLock, Dictionary<string, TrackStateEntry> stateIndex, StatusDisplay statusDisplay, bool fullVerify = false)
{
	var verifyWorker = new WorkerQueue<TrackInfo>(
		maxConcurrentVerifies,
		async (track, ct) =>
		{
			if (track.Status == TrackStatus.Verified)
			{
				return;
			}

			var verification = await verifier.VerifyAsync(track, config, ct, fullVerify).ConfigureAwait(false);
			if (!verification.Success)
			{
				track.Status = TrackStatus.Failed;
				track.LastError = verification.Error;
				errorReporter.RecordError(track.SourcePath, track.RelativePath, track.LastError ?? "Verification failed.", TrackStatus.Failed);
				lock (stateLock)
				{
					UpdateState(stateIndex, track, null);
				}
				logger.Error($"Verification failed for {track.RelativePath}");
				logger.Error($"  Reason: {track.LastError}");
				return;
			}

			track.Status = TrackStatus.Verified;
			lock (stateLock)
			{
				UpdateState(stateIndex, track, DateTime.UtcNow);
			}
		},
		logger
	);

	// Start background task to display progress
	var displayTask = Task.Run(async () =>
	{
		while (true)
		{
			var (processed, total) = verifyWorker.GetProgress();
			if (total > 0 && processed < total)
			{
				var currentTrack = tracksToVerify.FirstOrDefault(t => t.Status == TrackStatus.Converted);
				var jobName = currentTrack?.RelativePath ?? "idle";
				statusDisplay.UpdateStatus("Verifying", processed, total, jobName);
				statusDisplay.Render();
			}

			if (processed >= total && total > 0)
			{
				break;
			}

			await Task.Delay(500).ConfigureAwait(false);
		}
	});

	await verifyWorker.ProcessAsync(tracksToVerify, cancellationToken).ConfigureAwait(false);
	await displayTask.ConfigureAwait(false);
	
	// Save state after verification batch completes
	lock (stateLock)
	{
		// Final state save after all verifications
	}
	await stateStore.SaveAsync(BuildState(stateIndex)).ConfigureAwait(false);
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

