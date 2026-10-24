namespace CasCap.Models;

/// <summary>Summarizes one agent's active and latest published definitions for discovery.</summary>
public sealed record AgentDefinitionSummaryItem
{
    /// <summary>Gets the stable tenant-local agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the active definition, or <see langword="null" /> when the agent is inactive.</summary>
    public AgentDefinition? ActiveDefinition { get; init; }

    /// <summary>Gets the most recently published version.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Gets when the most recent version was published.</summary>
    public DateTimeOffset? LatestPublishedAtUtc { get; init; }

    /// <summary>Gets who published the most recent version.</summary>
    public string? LatestPublishedBy { get; init; }

    /// <summary>Gets how many immutable versions have been published.</summary>
    public int VersionCount { get; init; }
}
