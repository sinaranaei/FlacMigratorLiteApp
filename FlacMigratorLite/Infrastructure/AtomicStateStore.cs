using FlacMigratorLite.Models;

namespace FlacMigratorLite.Infrastructure;

// Thread-safe state store with atomic writes to prevent corruption from concurrent updates.
public class AtomicStateStore
{
    private readonly string _stateFilePath;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);

    public AtomicStateStore(string stateFilePath, Logger logger)
    {
        _stateFilePath = stateFilePath;
        _logger = logger;
    }

    public MigrationState Load()
    {
        var store = new StateStore(_stateFilePath, _logger);
        return store.Load();
    }

    public async Task<MigrationState> LoadAsync()
    {
        await _readLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return Load();
        }
        finally
        {
            _readLock.Release();
        }
    }

    public async Task SaveAsync(MigrationState state)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = new StateStore(_stateFilePath, _logger);
            store.Save(state);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
