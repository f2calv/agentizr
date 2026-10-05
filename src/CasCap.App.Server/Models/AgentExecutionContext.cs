namespace CasCap.Models;

/// <summary>Fully resolved state supplied to an <see cref="IAgentExecutor" />.</summary>
public sealed record AgentExecutionContext
{
    /// <summary>Gets the original execution request.</summary>
    public required AgentExecutionRequest Request { get; init; }

    /// <summary>Gets the resolved versioned definition.</summary>
    public required AgentDefinition Definition { get; init; }

    /// <summary>Gets the provider credential, when required.</summary>
    public string? ProviderApiKey { get; init; }

    /// <summary>Gets the serialized prior session state.</summary>
    public string? SessionStateJson { get; init; }

    /// <summary>Gets the per-session overrides.</summary>
    public required AgentOverrideState Overrides { get; init; }
}
