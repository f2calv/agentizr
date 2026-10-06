namespace CasCap.Services;

/// <summary>Owns tenant-scoped session, snapshot, and runtime-override control operations.</summary>
public sealed class AgentSessionControlService(
    IAgentDefinitionStore definitionStore,
    IProviderCredentialStore credentialStore,
    IAgentSessionStore sessionStore,
    IAgentOverrideStore overrideStore,
    IAgentSessionCodec sessionCodec)
{
    /// <summary>Gets active session status without exposing serialized framework state.</summary>
    public async ValueTask<AgentSessionStatus> GetStatusAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(agentName, sessionId);
        var definition = await definitionStore.GetAsync(agentName, cancellationToken);
        if (definition is null)
            return new AgentSessionStatus();

        var overrides = await overrideStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
        var stateJson = await sessionStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
        if (stateJson is null)
        {
            return new AgentSessionStatus
            {
                AgentExists = true,
                SessionEnabled = overrides.SessionEnabled,
            };
        }

        var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, cancellationToken);
        return new AgentSessionStatus
        {
            AgentExists = true,
            SessionExists = true,
            SessionEnabled = overrides.SessionEnabled,
            Inspection = await sessionCodec.InspectAsync(definition, apiKey, stateJson, cancellationToken),
        };
    }

    /// <summary>Deletes active session state while preserving runtime overrides.</summary>
    public async ValueTask<bool> ResetAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        await sessionStore.DeleteAsync(agentName, definition.Version, sessionId, cancellationToken);
        return true;
    }

    /// <summary>Compacts active session history using Agent Framework serialization.</summary>
    public async ValueTask<AgentSessionCompactionStatus> CompactAsync(
        string agentName,
        string sessionId,
        int retainMessageCount,
        CancellationToken cancellationToken)
    {
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return new AgentSessionCompactionStatus();

        var stateJson = await sessionStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
        if (stateJson is null)
            return new AgentSessionCompactionStatus { AgentExists = true };

        var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, cancellationToken);
        var result = await sessionCodec.CompactAsync(
            definition,
            apiKey,
            stateJson,
            retainMessageCount,
            cancellationToken);
        if (result.HistoryAvailable)
        {
            await sessionStore.SetAsync(
                agentName,
                definition.Version,
                sessionId,
                result.SessionStateJson,
                cancellationToken);
        }

        return new AgentSessionCompactionStatus
        {
            AgentExists = true,
            SessionExists = true,
            HistoryAvailable = result.HistoryAvailable,
            RemovedMessageCount = result.RemovedMessageCount,
        };
    }

    /// <summary>Saves the active session as a named snapshot.</summary>
    public async ValueTask<bool> SaveSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
    {
        ValidateSnapshotName(snapshotName);
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        var stateJson = await sessionStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
        if (stateJson is null)
            return false;
        await sessionStore.SetSnapshotAsync(
            agentName,
            definition.Version,
            sessionId,
            snapshotName,
            stateJson,
            cancellationToken);
        return true;
    }

    /// <summary>Loads a named snapshot into the active session.</summary>
    public async ValueTask<bool> LoadSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
    {
        ValidateSnapshotName(snapshotName);
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        var stateJson = await sessionStore.GetSnapshotAsync(
            agentName,
            definition.Version,
            sessionId,
            snapshotName,
            cancellationToken);
        if (stateJson is null)
            return false;
        await sessionStore.SetAsync(agentName, definition.Version, sessionId, stateJson, cancellationToken);
        return true;
    }

    /// <summary>Deletes a named session snapshot.</summary>
    public async ValueTask<bool> DeleteSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
    {
        ValidateSnapshotName(snapshotName);
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        var stateJson = await sessionStore.GetSnapshotAsync(
            agentName,
            definition.Version,
            sessionId,
            snapshotName,
            cancellationToken);
        if (stateJson is null)
            return false;
        await sessionStore.DeleteSnapshotAsync(
            agentName,
            definition.Version,
            sessionId,
            snapshotName,
            cancellationToken);
        return true;
    }

    /// <summary>Gets the complete runtime override state.</summary>
    public async ValueTask<AgentOverrideState?> GetOverridesAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        return definition is null
            ? null
            : await overrideStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
    }

    /// <summary>Replaces the complete runtime override state.</summary>
    public async ValueTask<bool> SetOverridesAsync(
        string agentName,
        string sessionId,
        AgentOverrideState overrides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        await overrideStore.SetAsync(agentName, definition.Version, sessionId, overrides, cancellationToken);
        return true;
    }

    private async ValueTask<AgentDefinition?> ResolveDefinitionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(agentName, sessionId);
        return await definitionStore.GetAsync(agentName, cancellationToken);
    }

    private static void ValidateIdentifiers(string agentName, string sessionId)
    {
        ValidateIdentifier(agentName, nameof(agentName));
        ValidateIdentifier(sessionId, nameof(sessionId));
    }

    private static void ValidateSnapshotName(string snapshotName) =>
        ValidateIdentifier(snapshotName, nameof(snapshotName));

    private static void ValidateIdentifier(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length > 200)
            throw new ArgumentException("The value cannot exceed 200 characters.", paramName);
    }
}
