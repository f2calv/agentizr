using CasCap.Common.Abstractions;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace CasCap.Tests.Unit;

/// <summary>Shared process-local implementation of the distributed cache contract for store tests.</summary>
internal sealed class InMemoryDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public event EventHandler<PostEvictionEventArgs> PostEvictionEvent
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public Task<T?> Get<T>(string key) where T : class =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : null);

    /// <inheritdoc/>
    public async Task<T?> Get<T>(
        string key,
        Func<Task<T>>? createItem = null,
        TimeSpan? slidingExpiration = null,
        DateTimeOffset? absoluteExpiration = null,
        CommandFlags flags = CommandFlags.None) where T : class
    {
        if (_values.TryGetValue(key, out var value))
            return (T)value;
        if (createItem is null)
            return null;

        var created = await createItem();
        _values[key] = created;
        return created;
    }

    /// <inheritdoc/>
    public Task Set<T>(string key, T cacheEntry) where T : class
    {
        _values[key] = cacheEntry;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Set<T>(
        string key,
        T cacheEntry,
        TimeSpan? slidingExpiration = null,
        DateTimeOffset? absoluteExpiration = null,
        CommandFlags flags = CommandFlags.None) where T : class =>
        Set(key, cacheEntry);

    /// <inheritdoc/>
    public Task<bool> Delete(string key, CommandFlags flags = CommandFlags.None) =>
        Task.FromResult(_values.TryRemove(key, out _));

    /// <inheritdoc/>
    public Task<long> DeleteAll(CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = _values.Count;
        _values.Clear();
        return Task.FromResult((long)count);
    }
}
