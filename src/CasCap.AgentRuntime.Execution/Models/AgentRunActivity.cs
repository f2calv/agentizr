namespace CasCap.AgentRuntime.Execution.Models;

/// <summary>Host-observable message or tool activity, without framework or provider configuration.</summary>
public sealed record AgentRunActivity
{
    /// <summary>Gets the activity category.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the executing agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the delegation depth.</summary>
    public int Depth { get; init; }

    /// <summary>Gets the tool call identity when applicable.</summary>
    public string? CallId { get; init; }

    /// <summary>Gets the tool name when applicable.</summary>
    public string? ToolName { get; init; }

    /// <summary>Gets the projected activity data; the host must apply its storage privacy policy.</summary>
    public object? Data { get; init; }
}
