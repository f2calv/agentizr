namespace CasCap.Services;

/// <summary>Coordinates tenant-scoped definition, credential, session, override, and execution services.</summary>
public sealed class AgentExecutionCoordinator(
    IAgentDefinitionStore definitionStore,
    IProviderCredentialStore credentialStore,
    IAgentSessionStore sessionStore,
    IAgentOverrideStore overrideStore,
    IAgentExecutor executor)
{
    /// <summary>Executes one agent turn and persists returned session state when enabled.</summary>
    public async ValueTask<AgentExecutionResult?> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(request.AgentName, cancellationToken);
        if (definition is null)
            return null;

        var overrides = await overrideStore.GetAsync(request.AgentName, request.SessionId, cancellationToken);
        var sessionStateJson = overrides.SessionEnabled
            ? await sessionStore.GetAsync(request.AgentName, request.SessionId, cancellationToken)
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

        if (overrides.SessionEnabled && result.SessionStateJson is not null)
            await sessionStore.SetAsync(request.AgentName, request.SessionId, result.SessionStateJson, cancellationToken);

        return result with { DefinitionVersion = definition.Version };
    }
}
