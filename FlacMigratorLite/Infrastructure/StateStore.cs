using System.Text.Json;
using FlacMigratorLite.Models;

namespace FlacMigratorLite.Infrastructure;

// Persists migration progress to disk.
// Allows safe resume after crash.
public class StateStore
{
    private readonly string _stateFilePath;
    private readonly Logger _logger;

    public StateStore(string stateFilePath, Logger logger)
    {
        _stateFilePath = stateFilePath;
        _logger = logger;
    }

    public MigrationState Load()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new MigrationState();
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<MigrationState>(json, JsonOptions()) ?? new MigrationState();
            return state;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load state file. Starting fresh. {ex.Message}");
            return new MigrationState();
        }
    }

    public void Save(MigrationState state)
    {
        state.UpdatedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOptions());
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_stateFilePath, json);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }
}
