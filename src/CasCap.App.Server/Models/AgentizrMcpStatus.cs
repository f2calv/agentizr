using System.ComponentModel;

namespace CasCap.Models;

/// <summary>Identity and clock of the running Agent Runtime, without tenant or definition state.</summary>
public sealed record AgentizrMcpStatus
{
    /// <summary>Service name.</summary>
    [Description("Service name; always 'agentizr'.")]
    public required string Service { get; init; }

    /// <summary>Deployed application version.</summary>
    [Description("Deployed application version.")]
    public required string Version { get; init; }

    /// <summary>Source commit the build was produced from.</summary>
    [Description("Source commit the build was produced from.")]
    public required string Commit { get; init; }

    /// <summary>Current time on the runtime.</summary>
    [Description("Current time on the runtime, in UTC.")]
    public required DateTimeOffset UtcNow { get; init; }
}
