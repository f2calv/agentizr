using CasCap.Entities;
using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>PostgreSQL authority for immutable tenant agent definitions and their active pointers.</summary>
internal sealed class PostgresAgentDefinitionStore(
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IAgentDefinitionStore, IAgentDefinitionAdministrationStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Include(pointer => pointer.Snapshot)
            .SingleOrDefaultAsync(
                pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName,
                cancellationToken);
        return active?.Snapshot is { } snapshot ? Deserialize(snapshot.DefinitionJson) : null;
    }

    /// <summary>Gets only the active definition version for cache selection.</summary>
    public async ValueTask<string?> GetActiveVersionAsync(
        string agentName,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName)
            .Select(pointer => pointer.Snapshot!.DefinitionVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Validator.ValidateObject(definition, new ValidationContext(definition), validateAllProperties: true);
        Validator.ValidateObject(definition.Agent, new ValidationContext(definition.Agent), validateAllProperties: true);
        Validator.ValidateObject(definition.Provider, new ValidationContext(definition.Provider), validateAllProperties: true);
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        ValidateOptional(publishedBy, nameof(publishedBy), 200);
        ValidateOptional(changeReason, nameof(changeReason), 2_000);
        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var duplicate = await dbContext.AgentDefinitionSnapshots.AnyAsync(
            snapshot => snapshot.TenantId == tenantContext.TenantId
                && snapshot.AgentName == definition.Name
                && snapshot.DefinitionVersion == definition.Version,
            cancellationToken);
        if (duplicate)
            throw new AgentDefinitionVersionConflictException();

        var snapshot = new AgentDefinitionSnapshotEntity
        {
            TenantId = tenantContext.TenantId,
            AgentName = definition.Name,
            DefinitionVersion = definition.Version,
            SchemaVersion = schemaVersion,
            DefinitionJson = JsonSerializer.Serialize(
                definition with { Provider = definition.Provider with { ApiKey = null } },
                JsonSerializerOptions.Web),
            PublishedAtUtc = timeProvider.GetUtcNow(),
            PublishedBy = publishedBy,
            ChangeReason = changeReason,
        };
        dbContext.AgentDefinitionSnapshots.Add(snapshot);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new AgentDefinitionVersionConflictException(ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Include(pointer => pointer.Snapshot)
            .SingleOrDefaultAsync(
                pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName,
                cancellationToken);
        return active?.Snapshot is { } snapshot ? MapSnapshot(snapshot, isActive: true) : null;
    }

    /// <inheritdoc/>
    public async ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ValidateKey(definitionVersion, nameof(definitionVersion), 100);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshot = await dbContext.AgentDefinitionSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantContext.TenantId
                    && item.AgentName == agentName
                    && item.DefinitionVersion == definitionVersion,
                cancellationToken);
        if (snapshot is null)
            return null;
        var isActive = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .AnyAsync(
                pointer => pointer.TenantId == tenantContext.TenantId
                    && pointer.AgentName == agentName
                    && pointer.SnapshotId == snapshot.Id,
                cancellationToken);
        return MapSnapshot(snapshot, isActive);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var activeSnapshotId = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName)
            .Select(pointer => (long?)pointer.SnapshotId)
            .SingleOrDefaultAsync(cancellationToken);
        return await dbContext.AgentDefinitionSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.TenantId == tenantContext.TenantId && snapshot.AgentName == agentName)
            .OrderByDescending(snapshot => snapshot.Id)
            .Take(limit)
            .Select(snapshot => new AgentDefinitionHistoryItem
            {
                DefinitionVersion = snapshot.DefinitionVersion,
                SchemaVersion = snapshot.SchemaVersion,
                PublishedAtUtc = snapshot.PublishedAtUtc,
                PublishedBy = snapshot.PublishedBy,
                ChangeReason = snapshot.ChangeReason,
                IsActive = snapshot.Id == activeSnapshotId,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Holds a session-level PostgreSQL advisory lock on a dedicated connection. Other providers, used
    /// only by tests, receive a no-op handle.
    /// </remarks>
    public async ValueTask<IAsyncDisposable> AcquireTenantActivationLockAsync(CancellationToken cancellationToken)
    {
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (dbContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await dbContext.DisposeAsync();
            return NoOpAsyncDisposable.Instance;
        }

        var lockKey = $"activation:{tenantContext.TenantId.Length}:{tenantContext.TenantId}";
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
        return new TenantActivationLock(dbContext, lockKey);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken,
        ExpectedActiveDefinition? expectedActive = null)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ValidateKey(definitionVersion, nameof(definitionVersion), 100);
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        ValidateOptional(activatedBy, nameof(activatedBy), 200);
        ValidateOptional(changeReason, nameof(changeReason), 2_000);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireAgentLockAsync(dbContext, agentName, cancellationToken);
        var snapshot = await dbContext.AgentDefinitionSnapshots.SingleOrDefaultAsync(
            item => item.TenantId == tenantContext.TenantId
                && item.AgentName == agentName
                && item.DefinitionVersion == definitionVersion,
            cancellationToken);
        if (snapshot is null)
            return false;

        var activatedAtUtc = timeProvider.GetUtcNow();
        await EnsureExpectedActiveAsync(dbContext, agentName, expectedActive, cancellationToken);
        var active = await dbContext.ActiveAgentDefinitions.SingleOrDefaultAsync(
            pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName,
            cancellationToken);
        if (active is null)
        {
            dbContext.ActiveAgentDefinitions.Add(new ActiveAgentDefinitionEntity
            {
                TenantId = tenantContext.TenantId,
                AgentName = agentName,
                SnapshotId = snapshot.Id,
                UpdatedAtUtc = activatedAtUtc,
            });
        }
        else
        {
            active.SnapshotId = snapshot.Id;
            active.UpdatedAtUtc = activatedAtUtc;
        }

        dbContext.AgentDefinitionActivations.Add(new AgentDefinitionActivationEntity
        {
            TenantId = tenantContext.TenantId,
            AgentName = agentName,
            SnapshotId = snapshot.Id,
            ActivatedAtUtc = activatedAtUtc,
            ActivatedBy = activatedBy,
            ChangeReason = changeReason,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.AgentDefinitionActivations
            .AsNoTracking()
            .Where(activation => activation.TenantId == tenantContext.TenantId && activation.AgentName == agentName)
            .OrderByDescending(activation => activation.Id)
            .Take(limit)
            .Select(activation => new AgentDefinitionActivationItem
            {
                DefinitionVersion = activation.Snapshot!.DefinitionVersion,
                ActivatedAtUtc = activation.ActivatedAtUtc,
                ActivatedBy = activation.ActivatedBy,
                ChangeReason = activation.ChangeReason,
                IsDeactivation = activation.IsDeactivation,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeactivateAsync(
        string agentName,
        string? deactivatedBy,
        string changeReason,
        CancellationToken cancellationToken,
        ExpectedActiveDefinition? expectedActive = null)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        ValidateKey(changeReason, nameof(changeReason), 2_000);
        ValidateOptional(deactivatedBy, nameof(deactivatedBy), 200);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireAgentLockAsync(dbContext, agentName, cancellationToken);
        await EnsureExpectedActiveAsync(dbContext, agentName, expectedActive, cancellationToken);
        var active = await dbContext.ActiveAgentDefinitions.SingleOrDefaultAsync(
            pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName,
            cancellationToken);
        if (active is null)
            return false;

        dbContext.ActiveAgentDefinitions.Remove(active);
        dbContext.AgentDefinitionActivations.Add(new AgentDefinitionActivationEntity
        {
            TenantId = tenantContext.TenantId,
            AgentName = agentName,
            SnapshotId = active.SnapshotId,
            ActivatedAtUtc = timeProvider.GetUtcNow(),
            ActivatedBy = deactivatedBy,
            ChangeReason = changeReason,
            IsDeactivation = true,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken)
    {
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var json = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantContext.TenantId)
            .Select(pointer => pointer.Snapshot!.DefinitionJson)
            .ToListAsync(cancellationToken);
        return [.. json.Select(Deserialize)];
    }

    /// <inheritdoc/>
    public async ValueTask<AgentDefinitionSummaryPage> GetAgentsAsync(
        string? nameFilter,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        ValidateKey(tenantId, nameof(tenantContext.TenantId), 200);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ValidateOptional(nameFilter, nameof(nameFilter), 200);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = dbContext.AgentDefinitionSnapshots.AsNoTracking().Where(snapshot => snapshot.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var filter = nameFilter.Trim().ToLowerInvariant();
            snapshots = snapshots.Where(snapshot => snapshot.AgentName.ToLower().Contains(filter));
        }
        var agents = snapshots
            .GroupBy(snapshot => snapshot.AgentName)
            .Select(group => new { AgentName = group.Key, VersionCount = group.Count(), LatestId = group.Max(snapshot => snapshot.Id) });
        var totalCount = await agents.CountAsync(cancellationToken);
        var page = await agents.OrderBy(agent => agent.AgentName).Skip(skip).Take(take).ToListAsync(cancellationToken);
        if (page.Count == 0)
            return new AgentDefinitionSummaryPage([], totalCount);

        var latestIds = page.Select(agent => agent.LatestId).ToArray();
        var latest = await dbContext.AgentDefinitionSnapshots
            .AsNoTracking()
            .Where(snapshot => latestIds.Contains(snapshot.Id))
            .Select(snapshot => new { snapshot.AgentName, snapshot.DefinitionVersion, snapshot.PublishedAtUtc, snapshot.PublishedBy })
            .ToDictionaryAsync(snapshot => snapshot.AgentName, StringComparer.Ordinal, cancellationToken);
        var agentNames = page.Select(agent => agent.AgentName).ToArray();
        var active = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantId && agentNames.Contains(pointer.AgentName))
            .Select(pointer => new { pointer.AgentName, pointer.Snapshot!.DefinitionJson })
            .ToDictionaryAsync(pointer => pointer.AgentName, pointer => pointer.DefinitionJson, StringComparer.Ordinal, cancellationToken);

        return new AgentDefinitionSummaryPage(
            [.. page.Select(agent => new AgentDefinitionSummaryItem
            {
                AgentName = agent.AgentName,
                ActiveDefinition = active.TryGetValue(agent.AgentName, out var json) ? Deserialize(json) : null,
                LatestVersion = latest[agent.AgentName].DefinitionVersion,
                LatestPublishedAtUtc = latest[agent.AgentName].PublishedAtUtc,
                LatestPublishedBy = latest[agent.AgentName].PublishedBy,
                VersionCount = agent.VersionCount,
            })],
            totalCount);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<string, int>> CountAgentsAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        if (tenantIds.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.AgentDefinitionSnapshots
            .AsNoTracking()
            .Where(snapshot => tenantIds.Contains(snapshot.TenantId))
            .GroupBy(snapshot => snapshot.TenantId)
            .Select(group => new { TenantId = group.Key, Count = group.Select(snapshot => snapshot.AgentName).Distinct().Count() })
            .ToDictionaryAsync(group => group.TenantId, group => group.Count, StringComparer.Ordinal, cancellationToken);
    }

    private async Task EnsureExpectedActiveAsync(
        AgentRuntimeDbContext dbContext,
        string agentName,
        ExpectedActiveDefinition? expectedActive,
        CancellationToken cancellationToken)
    {
        if (expectedActive is null)
            return;
        var activeVersion = await dbContext.ActiveAgentDefinitions
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantContext.TenantId && pointer.AgentName == agentName)
            .Select(pointer => pointer.Snapshot!.DefinitionVersion)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.Equals(expectedActive.Version, activeVersion, StringComparison.Ordinal))
            throw new AgentDefinitionActiveVersionConflictException();
    }

    private async Task AcquireAgentLockAsync(AgentRuntimeDbContext dbContext, string agentName, CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            return;
        var lockKey = $"{tenantContext.TenantId.Length}:{tenantContext.TenantId}"
            + $"{agentName.Length}:{agentName}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private static AgentDefinition Deserialize(string definitionJson) =>
        JsonSerializer.Deserialize<AgentDefinition>(definitionJson, JsonSerializerOptions.Web)
        ?? throw new InvalidDataException("Stored agent definition JSON is invalid.");

    private static AgentDefinitionSnapshotItem MapSnapshot(
        AgentDefinitionSnapshotEntity snapshot,
        bool isActive) =>
        new()
        {
            Definition = Deserialize(snapshot.DefinitionJson),
            SchemaVersion = snapshot.SchemaVersion,
            PublishedAtUtc = snapshot.PublishedAtUtc,
            PublishedBy = snapshot.PublishedBy,
            ChangeReason = snapshot.ChangeReason,
            IsActive = isActive,
        };

    private static void ValidateKey(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length > maxLength)
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", paramName);
    }

    private static void ValidateOptional(string? value, string paramName, int maxLength)
    {
        if (value?.Length > maxLength)
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", paramName);
    }

    private sealed class TenantActivationLock(AgentRuntimeDbContext dbContext, string lockKey) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_unlock(hashtextextended({lockKey}, 0))");
            }
            finally
            {
                // Closing the session also releases the lock if the unlock statement failed.
                await dbContext.DisposeAsync();
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoOpAsyncDisposable Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
