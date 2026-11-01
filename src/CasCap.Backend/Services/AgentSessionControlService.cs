using CasCap.Common.Extensions;
using CasCap.Exceptions;

namespace CasCap.Services;

/// <summary>Owns tenant-scoped session, snapshot, and runtime-override control operations.</summary>
public sealed class AgentSessionControlService(
    IAgentDefinitionStore definitionStore,
    IProviderCredentialStore credentialStore,
    IAgentSessionStore sessionStore,
    IAgentOverrideStore overrideStore,
    IAgentSessionCodec sessionCodec,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext,
    IAgentSessionLock sessionLock)
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
        if (definition.Agent.Stateless)
            return new AgentSessionStatus { AgentExists = true, SessionEnabled = false };

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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
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
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken, allowStateless: true);
        if (definition is null)
            return null;
        return definition.Agent.Stateless
            ? new AgentOverrideState { SessionEnabled = false }
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
        await using var sessionLease = await sessionLock.AcquireAsync(agentName, sessionId, cancellationToken);
        var definition = await ResolveDefinitionAsync(agentName, sessionId, cancellationToken);
        if (definition is null)
            return false;
        await overrideStore.SetAsync(agentName, definition.Version, sessionId, overrides, cancellationToken);
        return true;
    }

    /// <summary>
    /// Gets the instructions the next run will use, or <see langword="null" /> when the agent is missing,
    /// has no instructions, or the tenant has not enabled disclosure.
    /// </summary>
    public async ValueTask<AgentInstructionsView?> GetInstructionsAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(agentName, sessionId);
        if (!runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            || !tenant.InstructionsDisclosureEnabled)
            return null;

        var definition = await definitionStore.GetAsync(agentName, cancellationToken);
        if (definition is null)
            return null;

        var overrides = definition.Agent.Stateless
            ? new AgentOverrideState { SessionEnabled = false }
            : await overrideStore.GetAsync(agentName, definition.Version, sessionId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(overrides.Instructions))
            return new AgentInstructionsView(definition.Version, true, overrides.Instructions);

        return string.IsNullOrWhiteSpace(definition.Agent.Instructions)
            && string.IsNullOrWhiteSpace(definition.Agent.InstructionsSource)
                ? null
                : new AgentInstructionsView(definition.Version, false, AgentExtensions.ResolveInstructions(definition.Agent));
    }

    private async ValueTask<AgentDefinition?> ResolveDefinitionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken,
        bool allowStateless = false)
    {
        ValidateIdentifiers(agentName, sessionId);
        var definition = await definitionStore.GetAsync(agentName, cancellationToken);
        if (!allowStateless && definition?.Agent.Stateless is true)
            throw new StatelessAgentSessionException();
        return definition;
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
