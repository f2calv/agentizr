using StackExchange.Redis;

namespace CasCap.Services;

/// <summary>Redis read-through cache over the PostgreSQL definition authority.</summary>
/// <remarks>
/// Immutable snapshots are cached separately from short-lived active pointers.
/// </remarks>
internal sealed class CachedAgentDefinitionStore(
    PostgresAgentDefinitionStore innerStore,
    IDistributedCache distributedCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ILogger<CachedAgentDefinitionStore> logger,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IAgentDefinitionStore, IAgentDefinitionAdministrationStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitionVersion = await GetActiveVersionAsync(agentName, cancellationToken);
        if (definitionVersion is null)
            return null;
        var key = AgentDefinitionCacheKey.Create(tenantContext.TenantId, agentName, definitionVersion);
        try
        {
            var cached = await distributedCache.Get<AgentDefinition>(key);
            if (cached is not null)
                return cached;
        }
        catch (RedisException)
        {
            LogCacheUnavailable();
        }

        var definition = (await innerStore.GetVersionAsync(agentName, definitionVersion, cancellationToken))?.Definition;
        if (definition is not null)
        {
            try
            {
                await distributedCache.Set(
                    key,
                    definition,
                    slidingExpiration: TimeSpan.FromMinutes(runtimeConfig.Value.DefinitionCacheExpirationMinutes));
            }
            catch (RedisException)
            {
                LogCacheUnavailable();
            }
        }
        return definition;
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        await innerStore.PublishAsync(
            definition,
            schemaVersion,
            publishedBy,
            changeReason,
            cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken) =>
        innerStore.GetActiveAsync(agentName, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken) =>
        innerStore.GetVersionAsync(agentName, definitionVersion, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        innerStore.GetHistoryAsync(agentName, limit, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IAsyncDisposable> AcquireTenantActivationLockAsync(CancellationToken cancellationToken) =>
        innerStore.AcquireTenantActivationLockAsync(cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        var changed = await innerStore.ActivateAsync(
            agentName,
            definitionVersion,
            activatedBy,
            changeReason,
            cancellationToken);
        if (changed)
            await InvalidatePointerAsync(agentName);
        return changed;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeactivateAsync(
        string agentName,
        string? deactivatedBy,
        string changeReason,
        CancellationToken cancellationToken)
    {
        var changed = await innerStore.DeactivateAsync(agentName, deactivatedBy, changeReason, cancellationToken);
        if (changed)
            await InvalidatePointerAsync(agentName);
        return changed;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken) =>
        innerStore.GetActiveDefinitionsAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        innerStore.GetActivationHistoryAsync(agentName, limit, cancellationToken);

    private async ValueTask<string?> GetActiveVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        var key = AgentDefinitionCacheKey.CreateActive(tenantContext.TenantId, agentName);
        var observedAt = timeProvider.GetUtcNow();
        if (runtimeConfig.Value.ActiveDefinitionCacheSeconds == 0)
            return await innerStore.GetActiveVersionAsync(agentName, cancellationToken);
        try
        {
            var cached = await distributedCache.Get<ActivePointer>(key);
            if (cached is not null && cached.ExpiresAtUtc > observedAt)
                return cached.Version;
        }
        catch (RedisException)
        {
            LogCacheUnavailable();
        }
        var version = await innerStore.GetActiveVersionAsync(agentName, cancellationToken);
        var expiresAt = observedAt.AddSeconds(runtimeConfig.Value.ActiveDefinitionCacheSeconds);
        if (expiresAt > timeProvider.GetUtcNow())
        {
            try
            {
                await distributedCache.Set(key, new ActivePointer(version, expiresAt), absoluteExpiration: expiresAt);
            }
            catch (RedisException)
            {
                LogCacheUnavailable();
            }
        }
        return version;
    }

    private async ValueTask InvalidatePointerAsync(string agentName)
    {
        try
        {
            await distributedCache.Delete(AgentDefinitionCacheKey.CreateActive(tenantContext.TenantId, agentName));
        }
        catch (RedisException)
        {
            LogCacheUnavailable();
        }
    }

    private void LogCacheUnavailable()
    {
        if (logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning("{ClassName} Definition cache unavailable; using PostgreSQL", nameof(CachedAgentDefinitionStore));
    }

    private sealed record ActivePointer(string? Version, DateTimeOffset ExpiresAtUtc);
}
