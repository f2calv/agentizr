using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CasCap.Services;

/// <summary>Reuses durable session storage and connection-bound locks for caller-qualified, voice-only context.</summary>
internal sealed class RealtimeConversationStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    /// <summary>How long completed voice utterances remain resumable and retained in live storage.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private const string StateKind = "voice";
    private const string SchemaVersion = "voice-v1";

    /// <summary>Builds a caller-qualified lock identifier from validated claims, never from client-supplied identity.</summary>
    internal static string? GetSessionId(ClaimsPrincipal? caller, Guid conversationId)
    {
        if (caller?.Identity?.IsAuthenticated is not true)
            return null;
        var issuer = caller.FindFirstValue("iss");
        var subject = caller.FindFirstValue("oid") ?? caller.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? caller.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)
            ? null
            : AgentStateKey.CreateRedis("voice-owner", issuer, subject, SchemaVersion, conversationId.ToString("N"));
    }

    /// <summary>Opens a new context or resumes only an unexpired context owned by this tenant, agent and caller.</summary>
    public async Task<RealtimeConversationLease?> OpenAsync(
        string tenantId, string agentName, string definitionVersion, ClaimsPrincipal? caller,
        Guid? conversationId, CancellationToken cancellationToken, string? retentionPolicyId = null)
    {
        var id = conversationId ?? Guid.NewGuid();
        if (id == Guid.Empty || GetSessionId(caller, id) is not { } sessionId)
            return null;
        var scope = scopeFactory.CreateAsyncScope();
        IAsyncDisposable? ownership = null;
        var transferred = false;
        try
        {
            // These services outlive the HTTP request and must not read its ambient tenant context.
            if (scope.ServiceProvider.GetService<IDbContextFactory<AgentRuntimeDbContext>>() is null)
                return null;
            var stateStore = ActivatorUtilities.CreateInstance<PostgresAgentStateStore>(
                scope.ServiceProvider, new FixedTenantContext(tenantId));
            ownership = await stateStore.AcquireAsync(agentName, sessionId, cancellationToken);
            var key = AgentStateKey.CreateRedis(StateKind, tenantId, agentName, SchemaVersion, sessionId);
            var context = await stateStore.GetAsync<RealtimeConversation>(key, cancellationToken, useCache: false);
            if (conversationId is not null && (context is null || context.ExpiresAt <= timeProvider.GetUtcNow()
                || context.DefinitionVersion != definitionVersion || context.RetentionPolicyId != retentionPolicyId))
                return null;
            if (context is null)
            {
                context = new RealtimeConversation
                {
                    DefinitionVersion = definitionVersion,
                    RetentionPolicyId = retentionPolicyId,
                    ExpiresAt = timeProvider.GetUtcNow() + Retention,
                };
                await stateStore.WriteAsync(agentName, sessionId, key, context, cancellationToken);
            }
            var lease = new RealtimeConversationLease(
                id, agentName, sessionId, key, context, stateStore, timeProvider, ownership, scope);
            ownership = null;
            transferred = true;
            return lease;
        }
        finally
        {
            if (ownership is not null)
                await ownership.DisposeAsync();
            // A returned lease owns its scope; otherwise release it here.
            if (!transferred)
                await scope.DisposeAsync();
        }
    }

    /// <summary>Removes expired live-storage projections in bounded batches without racing an active call.</summary>
    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IDbContextFactory<AgentRuntimeDbContext>>() is not { } factory)
            return;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var cutoff = timeProvider.GetUtcNow() - Retention;
        var expired = await db.AgentStates.AsNoTracking()
            .Where(state => state.Key.StartsWith("agentizr:v1:voice:") && state.UpdatedAtUtc <= cutoff)
            .OrderBy(state => state.UpdatedAtUtc).Take(100)
            .Select(state => new { state.Key, state.TenantId, state.AgentName, state.SessionId })
            .ToListAsync(cancellationToken);
        foreach (var state in expired)
        {
            var store = ActivatorUtilities.CreateInstance<PostgresAgentStateStore>(
                scope.ServiceProvider, new FixedTenantContext(state.TenantId));
            try
            {
                await using var ownership = await store.AcquireAsync(state.AgentName, state.SessionId, cancellationToken);
                var context = await store.GetAsync<RealtimeConversation>(state.Key, cancellationToken, useCache: false);
                if (context?.ExpiresAt <= timeProvider.GetUtcNow())
                    await store.WriteAsync<RealtimeConversation>(
                        state.AgentName, state.SessionId, state.Key, null, cancellationToken);
            }
            catch (AgentSessionConflictException)
            {
                // The active owner will update the projection; retry on a subsequent sweep.
            }
        }
    }
}
