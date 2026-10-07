namespace CasCap.Services;

/// <summary>Redis read-through cache over the PostgreSQL definition authority.</summary>
internal sealed class CachedAgentDefinitionStore(
    PostgresAgentDefinitionStore innerStore,
    IDistributedCache distributedCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ILogger<CachedAgentDefinitionStore> logger,
    ITenantContext tenantContext) : IAgentDefinitionStore, IAgentDefinitionAdministrationStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitionVersion = await innerStore.GetActiveVersionAsync(agentName, cancellationToken);
        if (definitionVersion is null)
            return null;
        var key = AgentDefinitionCacheKey.Create(tenantContext.TenantId, agentName, definitionVersion);
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
    public async ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        return await innerStore.ActivateAsync(
            agentName,
            definitionVersion,
            activatedBy,
            changeReason,
            cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        innerStore.GetActivationHistoryAsync(agentName, limit, cancellationToken);
}
