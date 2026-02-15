namespace FlacMigratorLite.Infrastructure;

// Console logger with timestamps and formatting for migration output.
public class Logger
{
    private readonly object _lock = new();

    public void Info(string message) => Write("ℹ INFO", message, ConsoleColor.Cyan);
    public void Warn(string message) => Write("⚠ WARN", message, ConsoleColor.Yellow);
    public void Error(string message) => Write("✗ ERROR", message, ConsoleColor.Red);
    public void Success(string message) => Write("✓ OK", message, ConsoleColor.Green);
    public void Progress(string message) => Write("→ PROGRESS", message, ConsoleColor.Magenta);

    private void Write(string level, string message, ConsoleColor color)
    {
        lock (_lock)
        {
            Console.ForegroundColor = color;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] {level,-12}");
            Console.ResetColor();
            Console.WriteLine($" {message}");
        }
    }
}
