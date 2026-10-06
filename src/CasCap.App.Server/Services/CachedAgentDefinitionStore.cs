namespace CasCap.Services;

/// <summary>Redis read-through cache over the PostgreSQL definition authority.</summary>
internal sealed class CachedAgentDefinitionStore(
    PostgresAgentDefinitionStore innerStore,
    IDistributedCache distributedCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ILogger<CachedAgentDefinitionStore> logger,
    ITenantContext tenantContext) : IAgentDefinitionStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = AgentDefinitionCacheKey.Create(tenantContext.TenantId, agentName);
        try
        {
            var cached = await distributedCache.Get<AgentDefinition>(key);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to read cached agent definition");
        }

        var definition = await innerStore.GetAsync(agentName, cancellationToken);
        if (definition is not null)
        {
            try
            {
                await distributedCache.Set(
                    key,
                    definition,
                    slidingExpiration: TimeSpan.FromMinutes(runtimeConfig.Value.DefinitionCacheExpirationMinutes));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to cache agent definition");
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
        try
        {
            await distributedCache.Delete(
                AgentDefinitionCacheKey.Create(tenantContext.TenantId, definition.Name));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to invalidate cached agent definition after publication");
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        innerStore.GetHistoryAsync(agentName, limit, cancellationToken);
}
