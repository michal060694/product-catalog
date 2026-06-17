using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure;

public class SharedTaskStore : ISharedTaskStore
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Product?>>> _inFlight = new();
    private readonly ILogger<SharedTaskStore> _logger;
    private readonly TimeSpan _timeout;

    public SharedTaskStore(ILogger<SharedTaskStore> logger, IOptions<CacheSettings> options)
    {
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(options.Value.InFlightTimeoutSeconds);
    }

    public Task<Product?> GetOrAddAsync(string key, Func<Task<Product?>> factory)
    {
        if (_inFlight.TryGetValue(key, out var existing))
        {
            _logger.LogInformation("InFlight REUSED for cache key {CacheKey}.", key);
            return existing.Value;
        }

        Lazy<Task<Product?>>? lazy = null;
        lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<Product?>>(() =>
        {
            _logger.LogInformation("InFlight CREATED for cache key {CacheKey}.", key);
            var task = factory();

            _ = task.ContinueWith(
                t =>
                {
                    _inFlight.TryRemove(key, out _);
                    _logger.LogInformation("InFlight COMPLETED for cache key {CacheKey}.", key);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            _ = Task.Delay(_timeout).ContinueWith(
                t => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Product?>>>(key, lazy!)),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            return task;
        }));

        return lazy.Value;
    }
}
