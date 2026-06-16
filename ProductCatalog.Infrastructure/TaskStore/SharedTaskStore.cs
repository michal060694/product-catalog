using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductCatalog.Domain.Entities;
using ProductCatalog.Domain.TaskStore;
using ProductCatalog.Infrastructure.Cache;

namespace ProductCatalog.Infrastructure.TaskStore;

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
            _logger.LogInformation("InFlight REUSED for key {Key}", key);
            return existing.Value;
        }

        var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<Product?>>(() =>
        {
            _logger.LogInformation("InFlight CREATED for key {Key}", key);
            var task = factory();

            _ = task.ContinueWith(
                t =>
                {
                    _inFlight.TryRemove(key, out _);
                    _logger.LogInformation("InFlight COMPLETED for key {Key}", key);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            // safety-net: remove the entry even if the factory never completes
            _ = Task.Delay(_timeout).ContinueWith(
                t => _inFlight.TryRemove(key, out _),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            return task;
        }));

        return lazy.Value;
    }
}
