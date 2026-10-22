using System.Diagnostics;
using CasCap.Exceptions;

namespace CasCap.Services;

/// <summary>Coordinates tenant-scoped definition, credential, session, override, and execution services.</summary>
public sealed class AgentExecutionCoordinator(
    AgentRuntimeMetrics metrics,
    ITenantContext tenantContext,
    IAgentDefinitionStore definitionStore,
    IProviderCredentialStore credentialStore,
    IAgentSessionStore sessionStore,
    IAgentOverrideStore overrideStore,
    IAgentSessionCodec sessionCodec,
    IAgentExecutor executor,
    IAgentSessionLock sessionLock,
    AgentRunHistoryService? historySvc = null)
{
    /// <summary>Executes one agent turn and persists returned session state when enabled.</summary>
    public async ValueTask<AgentExecutionResult?> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        AgentDefinition? definition = null;
        Guid? runId = null;
        IAsyncDisposable? sessionLease = null;
        try
        {
            sessionLease = await sessionLock.AcquireAsync(
                request.AgentName, request.SessionId, cancellationToken);
            definition = await definitionStore.GetAsync(request.AgentName, cancellationToken);
            if (definition is null)
            {
                metrics.RecordUnavailable(tenantContext.TenantId, request.AgentName, stopwatch.Elapsed);
                return null;
            }

            var overrides = await overrideStore.GetAsync(
                request.AgentName,
                definition.Version,
                request.SessionId,
                cancellationToken);
            var sessionEnabled = overrides.SessionEnabled && !request.BypassSession;
            var sessionStateJson = sessionEnabled
                ? await sessionStore.GetAsync(request.AgentName, definition.Version, request.SessionId, cancellationToken)
                : null;
            if (sessionEnabled)
                metrics.RecordHa(tenantContext.TenantId, "session.load", sessionStateJson is null ? "new" : "resumed");
            var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, cancellationToken);
            historySvc?.ExcludeSecret(apiKey);
            if (historySvc is not null)
                runId = await historySvc.StartAsync(
                    request, definition, overrides.ModelName ?? definition.Provider.ModelName, cancellationToken);

            var result = await executor.ExecuteAsync(new AgentExecutionContext
            {
                Request = request,
                Definition = definition,
                ProviderApiKey = apiKey,
                SessionStateJson = sessionStateJson,
                Overrides = overrides,
                HistorySink = runId is { } id && historySvc is not null
                    ? (activity, token) => historySvc.AppendAsync(id, activity, token)
                    : null,
            }, cancellationToken);

            AgentSessionInspection? sessionInspection = null;
            if (sessionEnabled && result.SessionStateJson is not null)
            {
                await sessionStore.SetAsync(
                    request.AgentName,
                    definition.Version,
                    request.SessionId,
                    result.SessionStateJson,
                    cancellationToken);
                sessionInspection = await sessionCodec.InspectAsync(
                    definition,
                    apiKey,
                    result.SessionStateJson,
                    cancellationToken);
            }

            var completed = result with
            {
                DefinitionVersion = definition.Version,
                ModelName = overrides.ModelName ?? definition.Provider.ModelName,
                SessionInspection = sessionInspection,
            };
            if (runId is { } completedId && historySvc is not null)
                await historySvc.FinishAsync(completedId, completed, null);
            metrics.RecordCompleted(
                tenantContext.TenantId,
                definition,
                completed.ModelName,
                stopwatch.Elapsed,
                completed.Diagnostics,
                completed.Events.Select(executionEvent => executionEvent.Diagnostics).OfType<AgentRunResult>());
            return completed;
        }
        catch (Exception error)
        {
            metrics.RecordFailed(tenantContext.TenantId, request.AgentName, definition,
                stopwatch.Elapsed, cancelled: error is OperationCanceledException,
                conflict: error is AgentSessionConflictException);
            if (runId is { } failedId && historySvc is not null)
            {
                try
                {
                    await historySvc.FinishAsync(failedId, null, error);
                }
                catch (Exception archiveError)
                {
                    throw new AggregateException("Turn failed and its terminal history could not be recorded.", error, archiveError);
                }
            }
            throw;
        }
        finally
        {
            if (sessionLease is not null)
                await sessionLease.DisposeAsync();
        }
    }
}
