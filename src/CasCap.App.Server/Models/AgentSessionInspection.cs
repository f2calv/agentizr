namespace CasCap.Models;

/// <summary>Runtime-owned inspection of serialized Agent Framework session state.</summary>
public sealed record AgentSessionInspection
{
    /// <summary>Gets the serialized payload size in bytes.</summary>
    public int SizeBytes { get; init; }

    /// <summary>Gets inspectable Agent Framework state-bag entries.</summary>
    public IReadOnlyList<AgentSessionEntryInspection> Entries { get; init; } = [];
}
