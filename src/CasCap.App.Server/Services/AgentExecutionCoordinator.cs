using System.Diagnostics;

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
    IAgentExecutor executor)
{
    /// <summary>Executes one agent turn and persists returned session state when enabled.</summary>
    public async ValueTask<AgentExecutionResult?> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        AgentDefinition? definition = null;
        try
        {
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
            var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, cancellationToken);

            var result = await executor.ExecuteAsync(new AgentExecutionContext
            {
                Request = request,
                Definition = definition,
                ProviderApiKey = apiKey,
                SessionStateJson = sessionStateJson,
                Overrides = overrides,
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
            metrics.RecordCompleted(
                tenantContext.TenantId,
                definition,
                completed.ModelName,
                stopwatch.Elapsed,
                completed.Diagnostics);
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            metrics.RecordFailed(tenantContext.TenantId, request.AgentName, definition, stopwatch.Elapsed, cancelled: true);
            throw;
        }
        catch
        {
            metrics.RecordFailed(tenantContext.TenantId, request.AgentName, definition, stopwatch.Elapsed, cancelled: false);
            throw;
        }
    }
}
