namespace CasCap.Models;

/// <summary>Resolved status of one tenant-scoped agent session.</summary>
public sealed record AgentSessionStatus
{
    /// <summary>Gets whether the requested agent definition exists.</summary>
    public bool AgentExists { get; init; }

    /// <summary>Gets whether active serialized session state exists.</summary>
    public bool SessionExists { get; init; }

    /// <summary>Gets whether session loading and persistence are enabled.</summary>
    public bool SessionEnabled { get; init; }

    /// <summary>Gets session inspection details when state exists.</summary>
    public AgentSessionInspection? Inspection { get; init; }
}
