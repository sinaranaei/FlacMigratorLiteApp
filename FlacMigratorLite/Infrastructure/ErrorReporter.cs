using FlacMigratorLite.Models;

namespace FlacMigratorLite.Infrastructure;

// Generates detailed error reports for failed tracks.
public class ErrorReporter
{
    private readonly string _reportFilePath;
    private readonly Logger _logger;
    private readonly List<ErrorEntry> _errors = new();
    private readonly object _lock = new();

    public ErrorReporter(string reportFilePath, Logger logger)
    {
        _reportFilePath = reportFilePath;
        _logger = logger;
    }

    public void RecordError(string trackPath, string relativePath, string error, TrackStatus status)
    {
        lock (_lock)
        {
            _errors.Add(new ErrorEntry
            {
                Timestamp = DateTime.Now,
                TrackPath = trackPath,
                RelativePath = relativePath,
                Error = error,
                Status = status
            });
        }
    }

    public async Task GenerateReportAsync()
    {
        if (_errors.Count == 0)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_reportFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = new List<string>
            {
                "FLAC MIGRATION ERROR REPORT",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                new string('=', 80),
                $"Total Errors: {_errors.Count}",
                ""
            };

            foreach (var error in _errors.OrderBy(e => e.RelativePath))
            {
                lines.Add($"Track: {error.RelativePath}");
                lines.Add($"Full Path: {error.TrackPath}");
                lines.Add($"Status: {error.Status}");
                lines.Add($"Error: {error.Error}");
                lines.Add($"Time: {error.Timestamp:yyyy-MM-dd HH:mm:ss}");
                lines.Add(new string('-', 80));
                lines.Add("");
            }

            await File.WriteAllLinesAsync(_reportFilePath, lines).ConfigureAwait(false);
            _logger.Info($"Error report saved to {Path.GetFileName(_reportFilePath)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to generate error report: {ex.Message}");
        }
    }

    public List<ErrorEntry> GetErrors()
    {
        lock (_lock)
        {
            return _errors.ToList();
        }
    }
}

public class ErrorEntry
{
    public DateTime Timestamp { get; set; }
    public string TrackPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public TrackStatus Status { get; set; }
}
