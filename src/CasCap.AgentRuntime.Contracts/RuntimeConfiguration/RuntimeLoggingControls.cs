namespace CasCap.AgentRuntime.Contracts.RuntimeConfiguration;

/// <summary>Typed, non-secret runtime controls for the host logging pipeline.</summary>
public sealed class RuntimeLoggingControls
{
    /// <summary>Gets or sets the pipeline minimum; category overrides remain deployment-owned.</summary>
    /// <example>Information</example>
    [JsonConverter(typeof(JsonStringEnumConverter<RuntimeLoggingLevel>))]
    public RuntimeLoggingLevel MinimumLevel { get; set; } = RuntimeLoggingLevel.Information;
}
