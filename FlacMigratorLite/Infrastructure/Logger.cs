namespace FlacMigratorLite.Infrastructure;

// Simple console logger with timestamps for migration output.
public class Logger
{
    private readonly object _lock = new();

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Success(string message) => Write("OK", message);

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level} {message}");
        }
    }
}
