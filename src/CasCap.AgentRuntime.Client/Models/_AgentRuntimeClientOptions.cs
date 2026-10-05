namespace CasCap.AgentRuntime.Client.Models;

/// <summary>Connection options for the Agent Runtime client.</summary>
public sealed record AgentRuntimeClientOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string ConfigurationSectionName = "CasCap:AgentRuntimeClientOptions";

    /// <summary>Gets the Agent Runtime service base address.</summary>
    [Required]
    public required Uri BaseAddress { get; init; }
}
