namespace FlacMigratorLite.Infrastructure;

// Thread-safe worker queue for parallel conversion and verification.
// Limits concurrent operations to avoid resource exhaustion.
public class WorkerQueue<T>
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Func<T, CancellationToken, Task> _processor;
    private readonly Logger _logger;
    private int _successCount;
    private int _failureCount;
    private int _processedCount;
    private int _totalCount;
    private readonly object _lockObj = new();

    public WorkerQueue(int maxConcurrency, Func<T, CancellationToken, Task> processor, Logger logger)
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _processor = processor;
        _logger = logger;
    }

    public async Task ProcessAsync(IEnumerable<T> items, CancellationToken cancellationToken)
    {
        var itemList = items.ToList();
        lock (_lockObj)
        {
            _totalCount = itemList.Count;
            _processedCount = 0;
        }
        
        var tasks = itemList.Select(item => ProcessItemAsync(item, cancellationToken)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ProcessItemAsync(T item, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _processor(item, cancellationToken).ConfigureAwait(false);
            lock (_lockObj)
            {
                _successCount++;
                _processedCount++;
            }
        }
        catch (Exception ex)
        {
            lock (_lockObj)
            {
                _failureCount++;
                _processedCount++;
            }
            _logger.Error($"Worker queue error: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public (int Success, int Failure) GetStats()
    {
        lock (_lockObj)
        {
            return (_successCount, _failureCount);
        }
    }

    public (int Processed, int Total) GetProgress()
    {
        lock (_lockObj)
        {
            return (_processedCount, _totalCount);
        }
    }
}
