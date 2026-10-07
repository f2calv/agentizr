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
    public async ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        ValidateKey(agentName, nameof(agentName), 200);
        ValidateKey(definitionVersion, nameof(definitionVersion), 100);
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        ValidateOptional(activatedBy, nameof(activatedBy), 200);
        ValidateOptional(changeReason, nameof(changeReason), 2_000);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var lockKey = $"{tenantContext.TenantId.Length}:{tenantContext.TenantId}"
                + $"{agentName.Length}:{agentName}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
        var snapshot = await dbContext.AgentDefinitionSnapshots.SingleOrDefaultAsync(
            item => item.TenantId == tenantContext.TenantId
                && item.AgentName == agentName
                && item.DefinitionVersion == definitionVersion,
            cancellationToken);
        if (snapshot is null)
            return false;

        var activatedAtUtc = timeProvider.GetUtcNow();
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
            })
            .ToListAsync(cancellationToken);
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
}
