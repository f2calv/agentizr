namespace CasCap.Abstractions;

/// <summary>Resolves versioned agent definitions for the current tenant.</summary>
public interface IAgentDefinitionStore
{
    /// <summary>Gets the named definition, or <see langword="null" /> when it is not available to the tenant.</summary>
    ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken);
}
