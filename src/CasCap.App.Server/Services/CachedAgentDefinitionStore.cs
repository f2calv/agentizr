namespace CasCap.Services;

/// <summary>Redis read-through cache over the PostgreSQL definition authority.</summary>
/// <remarks>
/// Both the active-version pointer and the immutable version snapshots are cached, so a run needs no
/// database query while they are warm. Activation removes the pointer from every pod's cache.
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

    private async ValueTask<string?> GetActiveVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        var key = AgentDefinitionCacheKey.CreateActiveVersion(tenantContext.TenantId, agentName);
        try
        {
            if (await distributedCache.Get<string>(key) is { } cachedVersion)
                return cachedVersion;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to read cached active definition version");
        }

        var definitionVersion = await innerStore.GetActiveVersionAsync(agentName, cancellationToken);
        if (definitionVersion is not null)
        {
            try
            {
                await distributedCache.Set(
                    key,
                    definitionVersion,
                    absoluteExpiration: timeProvider.GetUtcNow().AddMinutes(runtimeConfig.Value.ActiveVersionCacheExpirationMinutes));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to cache active definition version");
            }
        }
        return definitionVersion;
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
        var activated = await innerStore.ActivateAsync(
            agentName,
            definitionVersion,
            activatedBy,
            changeReason,
            cancellationToken);
        if (activated)
            await InvalidateActiveVersionAsync(agentName);
        return activated;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeactivateAsync(
        string agentName,
        string? deactivatedBy,
        string changeReason,
        CancellationToken cancellationToken)
    {
        var deactivated = await innerStore.DeactivateAsync(agentName, deactivatedBy, changeReason, cancellationToken);
        if (deactivated)
            await InvalidateActiveVersionAsync(agentName);
        return deactivated;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken) =>
        innerStore.GetActiveDefinitionsAsync(cancellationToken);

    private async Task InvalidateActiveVersionAsync(string agentName)
    {
        try
        {
            await distributedCache.Delete(AgentDefinitionCacheKey.CreateActiveVersion(tenantContext.TenantId, agentName));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to invalidate the cached active definition version; it expires within {Minutes} minutes",
                runtimeConfig.Value.ActiveVersionCacheExpirationMinutes);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        innerStore.GetActivationHistoryAsync(agentName, limit, cancellationToken);
}
