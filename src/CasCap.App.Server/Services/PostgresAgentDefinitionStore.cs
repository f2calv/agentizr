using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>PostgreSQL authority for immutable tenant agent definitions and their active pointers.</summary>
internal sealed class PostgresAgentDefinitionStore(
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IAgentDefinitionStore
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
        ValidateKey(tenantContext.TenantId, nameof(tenantContext.TenantId), 200);
        ValidateOptional(publishedBy, nameof(publishedBy), 200);
        ValidateOptional(changeReason, nameof(changeReason), 2_000);
        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var duplicate = await dbContext.AgentDefinitionSnapshots.AnyAsync(
            snapshot => snapshot.TenantId == tenantContext.TenantId
                && snapshot.AgentName == definition.Name
                && snapshot.DefinitionVersion == definition.Version,
            cancellationToken);
        if (duplicate)
            throw new InvalidOperationException("The definition version already exists and is immutable.");

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
        await dbContext.SaveChangesAsync(cancellationToken);

        var active = await dbContext.ActiveAgentDefinitions.SingleOrDefaultAsync(
            pointer => pointer.TenantId == tenantContext.TenantId
                && pointer.AgentName == definition.Name,
            cancellationToken);
        if (active is null)
        {
            dbContext.ActiveAgentDefinitions.Add(new ActiveAgentDefinitionEntity
            {
                TenantId = tenantContext.TenantId,
                AgentName = definition.Name,
                SnapshotId = snapshot.Id,
                UpdatedAtUtc = timeProvider.GetUtcNow(),
            });
        }
        else
        {
            active.SnapshotId = snapshot.Id;
            active.UpdatedAtUtc = timeProvider.GetUtcNow();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
            })
            .ToListAsync(cancellationToken);
    }

    private static AgentDefinition Deserialize(string definitionJson) =>
        JsonSerializer.Deserialize<AgentDefinition>(definitionJson, JsonSerializerOptions.Web)
        ?? throw new InvalidDataException("Stored agent definition JSON is invalid.");

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
