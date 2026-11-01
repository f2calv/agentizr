using Azure;
using CasCap.Common.Services;
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Coalesces durable source requests and executes one fenced, bounded inventory without paid processing.</summary>
internal sealed partial class DocumentSynchronizationService(
    ILogger<DocumentSynchronizationService> logger,
    IOptions<DocumentIngestionConfig> config,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    IDocumentBlobInventory documentBlobInventory)
{
    /// <summary>Requests configured sources, optionally restricted to one server-authorized application.</summary>
    public Task RequestAsync(string? tenantId, CancellationToken token) =>
        RequestSourcesAsync(config.Value, timeProvider, dbContextFactory, tenantId, token);

    /// <summary>Persists requests without resolving inventory, transport or processing dependencies on an HTTP replica.</summary>
    internal static async Task RequestSourcesAsync(
        DocumentIngestionConfig settings, TimeProvider clock, IDbContextFactory<AgentRuntimeDbContext> database,
        string? tenantId, CancellationToken token)
    {
        if (!settings.SynchronizationEnabled)
            throw new InvalidOperationException("Document synchronization is disabled.");
        await using var db = await database.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, clock, token);
        foreach (var source in settings.Sources.Where(item => tenantId == null || item.TenantId == tenantId))
        {
            var id = SourceId(source);
            var state = await db.DocumentSynchronizations.SingleOrDefaultAsync(item => item.Id == id, token);
            if (state is null)
            {
                state = new()
                {
                    Id = id,
                    TenantId = source.TenantId,
                    CollectionId = source.CollectionId,
                    State = DocumentSynchronizationStates.Queued,
                };
                db.DocumentSynchronizations.Add(state);
            }
            if (state.State != DocumentSynchronizationStates.Running)
            {
                state.Requested = true;
                state.RequestedAtUtc = now;
            }
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    /// <summary>Claims at most one requested or interrupted source using database-enforced fencing.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ownershipToken)
    {
        if (!config.Value.SynchronizationEnabled)
            return false;
        var claim = await ClaimAsync(ownershipToken);
        if (claim is null)
            return false;
        try
        {
            await CoalescingScheduleRunner.RunWithTimeoutAsync(async token =>
            {
                await SynchronizeAsync(claim.Value.Source, claim.Value.RunId, token);
                return true;
            }, TimeSpan.FromSeconds(config.Value.SynchronizationTimeoutSeconds), timeProvider, ownershipToken);
            await FinishAsync(claim.Value.Source, claim.Value.RunId, null, ownershipToken);
        }
        catch (Exception exception) when (exception is RequestFailedException or IOException or TimeoutException)
        {
            ownershipToken.ThrowIfCancellationRequested();
            var failure = exception is TimeoutException
                ? DocumentSynchronizationStates.DeadlineExpired : DocumentSynchronizationStates.StorageUnavailable;
            LogFailure(logger, failure);
            await FinishAsync(claim.Value.Source, claim.Value.RunId, failure, ownershipToken);
        }
        return true;
    }

    /// <summary>Computes the stable identity of a trusted source mapping, without its Event Grid delivery identity.</summary>
    public static string SourceId(DocumentSourceConfig source) =>
        DocumentIngestionIdentity.Digest(source.TenantId, source.CollectionId, source.ContainerUri.TrimEnd('/'));

    private async Task<(DocumentSourceConfig Source, Guid RunId)?> ClaimAsync(CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        foreach (var source in config.Value.Sources)
        {
            var id = SourceId(source);
            var state = await db.DocumentSynchronizations.SingleOrDefaultAsync(item => item.Id == id, token);
            if (state is null || state.State == DocumentSynchronizationStates.Running && state.ExpiresAtUtc > now
                || !state.Requested && state.State != DocumentSynchronizationStates.Running)
                continue;
            state.RunId = Guid.NewGuid();
            state.ExpiresAtUtc = now.AddSeconds(config.Value.SynchronizationTimeoutSeconds);
            state.StartedAtUtc = now;
            state.FinishedAtUtc = null;
            state.Requested = false;
            state.State = DocumentSynchronizationStates.Running;
            state.FailureCode = null;
            state.AddedCount = state.RepairedCount = state.RemovedCount = state.UnchangedCount = state.ConflictCount = 0;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return (source, state.RunId.Value);
        }
        await transaction.CommitAsync(token);
        return null;
    }

    private async Task FinishAsync(DocumentSourceConfig source, Guid runId, string? failure, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var id = SourceId(source);
        var state = await db.DocumentSynchronizations.SingleAsync(item => item.Id == id, token);
        if (state.RunId != runId || state.State != DocumentSynchronizationStates.Running)
            return;
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        failure ??= state.ExpiresAtUtc <= now ? DocumentSynchronizationStates.DeadlineExpired : null;
        state.State = failure is null ? DocumentSynchronizationStates.Succeeded : DocumentSynchronizationStates.Failed;
        state.FailureCode = failure;
        state.FinishedAtUtc = now;
        if (failure is null)
            state.LastSucceededAtUtc = now;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task<DocumentSynchronizationEntity?> OwnedAsync(
        AgentRuntimeDbContext db, DocumentSourceConfig source, Guid runId, CancellationToken token)
    {
        var id = SourceId(source);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        return await db.DocumentSynchronizations.SingleOrDefaultAsync(item => item.Id == id
            && item.RunId == runId && item.State == DocumentSynchronizationStates.Running && item.ExpiresAtUtc > now, token);
    }

    [LoggerMessage(LogLevel.Warning, "{ClassName} source synchronization failed: {FailureCode}")]
    private static partial void LogFailure(ILogger logger, string failureCode,
        string className = nameof(DocumentSynchronizationService));
}
