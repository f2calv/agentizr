using Microsoft.Extensions.DependencyInjection;

namespace CasCap.Services;

/// <summary>Owns the PostgreSQL session lock and its scope for the entire lifetime of one resumable voice call.</summary>
internal sealed class RealtimeConversationLease(
    Guid id,
    string agentName,
    string sessionId,
    string key,
    RealtimeConversation conversation,
    PostgresAgentStateStore stateStore,
    TimeProvider timeProvider,
    IAsyncDisposable sessionLock,
    AsyncServiceScope scope) : IAsyncDisposable
{
    /// <summary>Gets the opaque conversation reference returned to its owner.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the retained bounded text context.</summary>
    public RealtimeConversation Conversation { get; } = conversation;

    /// <summary>Persists one completed transcript while still owning the exact connection-bound lock.</summary>
    public async Task RecordAsync(string itemId, string role, string text, CancellationToken cancellationToken)
    {
        Conversation.Record(itemId, role, text);
        Conversation.ExpiresAt = timeProvider.GetUtcNow() + RealtimeConversationStore.Retention;
        await stateStore.WriteAsync(agentName, sessionId, key, Conversation, cancellationToken);
    }

    /// <summary>Removes a truncated assistant utterance from durable replay context.</summary>
    public async Task TruncateAsync(string itemId, CancellationToken cancellationToken)
    {
        Conversation.Truncate(itemId);
        Conversation.ExpiresAt = timeProvider.GetUtcNow() + RealtimeConversationStore.Retention;
        await stateStore.WriteAsync(agentName, sessionId, key, Conversation, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await sessionLock.DisposeAsync();
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }
}
