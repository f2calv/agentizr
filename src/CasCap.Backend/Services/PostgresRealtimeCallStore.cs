using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>PostgreSQL realtime call records; every transition is a conditional update so concurrent replicas agree.</summary>
internal sealed class PostgresRealtimeCallStore(IDbContextFactory<AgentRuntimeDbContext> contextFactory) : IRealtimeCallStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string Active = nameof(RealtimeCallStatus.Active);
    private static readonly string Ending = nameof(RealtimeCallStatus.Ending);
    private static readonly string Ended = nameof(RealtimeCallStatus.Ended);

    /// <inheritdoc/>
    public async Task AddAsync(RealtimeCallRecord record, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.RealtimeCalls.Add(new RealtimeCallEntity
        {
            Id = record.CallId,
            TenantId = record.TenantId,
            AgentName = record.AgentName,
            OwnerId = record.OwnerId,
            Status = record.Status.ToString(),
            ProviderName = record.ProviderName,
            Endpoint = record.Endpoint.AbsoluteUri,
            ProviderCallId = record.ProviderCallId,
            StartedAtUtc = record.StartedAt,
            LeaseExpiresAtUtc = record.LeaseExpiresAt,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> RenewAsync(
        string callId, string ownerId, DateTimeOffset leaseExpiresAt, RealtimeCallUsage usage, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var usageJson = JsonSerializer.Serialize(usage, JsonOptions);
        return await context.RealtimeCalls
            .Where(call => call.Id == callId && call.OwnerId == ownerId && call.Status == Active)
            .ExecuteUpdateAsync(set => set
                .SetProperty(call => call.LeaseExpiresAtUtc, leaseExpiresAt)
                .SetProperty(call => call.UsageJson, usageJson), cancellationToken) == 1;
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(
        string callId, RealtimeCallSummaryResponse summary, DateTimeOffset endedAt, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var summaryJson = JsonSerializer.Serialize(summary, JsonOptions);
        await context.RealtimeCalls
            .Where(call => call.Id == callId && call.Status != Ended)
            .ExecuteUpdateAsync(set => set
                .SetProperty(call => call.Status, Ended)
                .SetProperty(call => call.EndedAtUtc, endedAt)
                .SetProperty(call => call.SummaryJson, summaryJson), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RealtimeCallRecord?> RequestEndAsync(
        string tenantId, string agentName, string callId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.RealtimeCalls
            .Where(call => call.Id == callId && call.TenantId == tenantId && call.AgentName == agentName && call.Status == Active)
            .ExecuteUpdateAsync(set => set.SetProperty(call => call.Status, Ending), cancellationToken);
        return await FindAsync(context, tenantId, agentName, callId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RealtimeCallRecord?> GetAsync(
        string tenantId, string agentName, string callId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindAsync(context, tenantId, agentName, callId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RealtimeCallRecord>> ClaimExpiredAsync(
        string claimantId, DateTimeOffset now, DateTimeOffset claimExpiresAt, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await context.RealtimeCalls.AsNoTracking()
            .Where(call => call.Status != Ended && call.LeaseExpiresAtUtc < now)
            .Take(20)
            .ToListAsync(cancellationToken);
        var claimed = new List<RealtimeCallRecord>();
        foreach (var candidate in candidates)
        {
            // Fenced on the observed owner and lease, so a concurrent claim or renewal wins and this one is skipped.
            var updated = await context.RealtimeCalls
                .Where(call => call.Id == candidate.Id && call.OwnerId == candidate.OwnerId
                    && call.LeaseExpiresAtUtc == candidate.LeaseExpiresAtUtc && call.Status != Ended)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(call => call.OwnerId, claimantId)
                    .SetProperty(call => call.LeaseExpiresAtUtc, claimExpiresAt), cancellationToken);
            if (updated == 1)
                claimed.Add(ToRecord(candidate) with { OwnerId = claimantId, LeaseExpiresAt = claimExpiresAt });
        }
        return claimed;
    }

    /// <inheritdoc/>
    public async Task DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.RealtimeCalls
            .Where(call => call.Status == Ended && call.EndedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<RealtimeCallRecord?> FindAsync(
        AgentRuntimeDbContext context, string tenantId, string agentName, string callId, CancellationToken cancellationToken)
    {
        var entity = await context.RealtimeCalls.AsNoTracking()
            .SingleOrDefaultAsync(call => call.Id == callId && call.TenantId == tenantId && call.AgentName == agentName, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    private static RealtimeCallRecord ToRecord(RealtimeCallEntity entity) => new()
    {
        CallId = entity.Id,
        TenantId = entity.TenantId,
        AgentName = entity.AgentName,
        OwnerId = entity.OwnerId,
        Status = Enum.Parse<RealtimeCallStatus>(entity.Status),
        ProviderName = entity.ProviderName,
        Endpoint = new Uri(entity.Endpoint),
        ProviderCallId = entity.ProviderCallId,
        StartedAt = entity.StartedAtUtc,
        LeaseExpiresAt = entity.LeaseExpiresAtUtc,
        Usage = entity.UsageJson is null ? null : JsonSerializer.Deserialize<RealtimeCallUsage>(entity.UsageJson, JsonOptions),
        Summary = entity.SummaryJson is null ? null : JsonSerializer.Deserialize<RealtimeCallSummaryResponse>(entity.SummaryJson, JsonOptions),
    };
}
