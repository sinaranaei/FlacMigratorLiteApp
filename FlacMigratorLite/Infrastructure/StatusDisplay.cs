namespace FlacMigratorLite.Infrastructure;

/// <summary>
/// Manages real-time status display at the bottom of the console.
/// Shows currently running jobs and progress counters.
/// </summary>
public class StatusDisplay
{
    private readonly Logger _logger;
    private volatile string _currentStatus = "";
    private volatile int _completed = 0;
    private volatile int _total = 0;
    private volatile string _phase = "";
    private readonly object _lock = new();

    public StatusDisplay(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>Update the status display with current job information.</summary>
    public void UpdateStatus(string phase, int completed, int total, string currentJob)
    {
        lock (_lock)
        {
            _phase = phase;
            _completed = completed;
            _total = total;
            _currentStatus = TruncateForConsole(currentJob, 70);
        }
    }

    /// <summary>Get formatted status line for display.</summary>
    public string GetStatusLine()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_phase))
            {
                return "";
            }

            var progress = _total > 0 ? $"{_completed}/{_total}" : "0/0";
            var percentage = _total > 0 ? (_completed * 100 / _total) : 0;
            var progressBar = BuildProgressBar(percentage, 20);

            return $"[{_phase,-12}] {progressBar} {progress,8} | {_currentStatus}";
        }
    }

    /// <summary>Display the status with proper refresh.</summary>
    public void Render()
    {
        var statusLine = GetStatusLine();
        if (!string.IsNullOrEmpty(statusLine))
        {
            // Clear the line and write status
            Console.Write($"\r{statusLine,-120}");
        }
    }

    /// <summary>Clear status display and reset state.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Console.Write("\r" + new string(' ', 120) + "\r");
            _currentStatus = "";
            _completed = 0;
            _total = 0;
            _phase = "";
        }
    }

    private static string BuildProgressBar(int percentage, int width)
    {
        var filled = (int)(width * percentage / 100.0);
        var empty = width - filled;
        return $"[{new string('█', filled)}{new string(' ', empty)}] {percentage,3}%";
    }

    private static string TruncateForConsole(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        // Truncate and add ellipsis
        var start = Math.Max(0, text.Length - maxLength + 3);
        return "..." + text.Substring(start);
    }
}
