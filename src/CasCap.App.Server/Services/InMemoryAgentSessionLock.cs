using CasCap.Exceptions;
using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>Provides process-local session coordination for credential-free Development hosts.</summary>
internal sealed class InMemoryAgentSessionLock
{
    private readonly ConcurrentDictionary<string, byte> _owners = new(StringComparer.Ordinal);

    /// <summary>Acquires one tenant-qualified Development session.</summary>
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string tenantId,
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = AgentStateKey.Create(tenantId, agentName, "session-lock", sessionId);
        if (!_owners.TryAdd(key, 0))
            throw new AgentSessionConflictException("The session is already being changed.");
        return ValueTask.FromResult<IAsyncDisposable>(new Lease(_owners, key));
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> owners, string key) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owners.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
