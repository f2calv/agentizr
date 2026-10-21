namespace CasCap.AgentRuntime.Client.Models;

/// <summary>Connection options for the Agent Runtime client.</summary>
public sealed record AgentRuntimeClientOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string ConfigurationSectionName = "CasCap:AgentRuntimeClientOptions";

    /// <summary>Gets the Agent Runtime service base address.</summary>
    [Required]
    public required Uri BaseAddress { get; init; }

    /// <summary>Gets the HTTP request timeout in minutes.</summary>
    /// <remarks>Defaults to ten minutes because model and tool runs may exceed the framework's 100-second default.</remarks>
    [Range(1, 60)]
    public int TimeoutMinutes { get; init; } = 10;
}
