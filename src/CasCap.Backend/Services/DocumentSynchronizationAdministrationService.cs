#if DEBUG
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Authorizes source requests through server-selected tenancy and projects only safe synchronization metadata.</summary>
public sealed class DocumentSynchronizationAdministrationService
{
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<DocumentIngestionConfig> _config;
    private readonly TimeProvider _clock;
    private readonly IDbContextFactory<AgentRuntimeDbContext>? _database;

    internal DocumentSynchronizationAdministrationService(
        ITenantContext tenantContext, IOptions<DocumentIngestionConfig> config, TimeProvider clock,
        IDbContextFactory<AgentRuntimeDbContext>? database)
    {
        _tenantContext = tenantContext;
        _config = config;
        _clock = clock;
        _database = database;
    }

    /// <summary>Reads configured application sources, or returns null when durable storage is unavailable.</summary>
    public async Task<DocumentSynchronizationResponse?> ReadAsync(CancellationToken token)
    {
        if (_database is null)
            return null;
        var settings = _config.Value;
        var sources = settings.Sources.Where(item => item.TenantId == _tenantContext.TenantId).ToArray();
        var ids = sources.Select(DocumentSynchronizationService.SourceId).ToArray();
        await using var db = await _database.CreateDbContextAsync(token);
        var states = await db.DocumentSynchronizations.AsNoTracking()
            .Where(item => item.TenantId == _tenantContext.TenantId && ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, token);
        return new()
        {
            Enabled = settings.SynchronizationEnabled,
            ProcessingEnabled = settings.ProcessingEnabled,
            Cron = settings.SynchronizationCron,
            Sources = sources.Select(source =>
            {
                var id = DocumentSynchronizationService.SourceId(source);
                states.TryGetValue(id, out var state);
                return new DocumentSynchronizationSourceResponse
                {
                    SourceId = id,
                    CollectionId = source.CollectionId,
                    State = state is null ? "NotRun"
                        : state.Requested && state.State != DocumentSynchronizationStates.Running
                            ? DocumentSynchronizationStates.Queued : state.State,
                    RequestedAtUtc = state?.RequestedAtUtc,
                    StartedAtUtc = state?.StartedAtUtc,
                    FinishedAtUtc = state?.FinishedAtUtc,
                    LastSucceededAtUtc = state?.LastSucceededAtUtc,
                    AddedCount = state?.AddedCount ?? 0,
                    RepairedCount = state?.RepairedCount ?? 0,
                    RemovedCount = state?.RemovedCount ?? 0,
                    UnchangedCount = state?.UnchangedCount ?? 0,
                    ConflictCount = state?.ConflictCount ?? 0,
                    FailureCode = state?.FailureCode,
                };
            }).OrderBy(item => item.CollectionId, StringComparer.Ordinal).ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>Coalesces requests for the current application's configured sources, never executing inventory in HTTP.</summary>
    public async Task<DocumentSynchronizationResponse?> RequestAsync(CancellationToken token)
    {
        if (_database is null || !_config.Value.SynchronizationEnabled
            || !_config.Value.Sources.Any(source => source.TenantId == _tenantContext.TenantId))
            return null;
        await DocumentSynchronizationService.RequestSourcesAsync(_config.Value, _clock, _database, _tenantContext.TenantId, token);
        return await ReadAsync(token);
    }
}
#endif
