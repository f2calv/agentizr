namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports the current tenant-scoped agent session state.</summary>
public sealed record AgentSessionInfoResponse
{
    /// <summary>Gets whether serialized session state exists.</summary>
    /// <example>true</example>
    public bool Exists { get; init; }

    /// <summary>Gets whether session loading and persistence are enabled.</summary>
    /// <example>true</example>
    public bool SessionEnabled { get; init; }

    /// <summary>Gets the serialized session size in bytes.</summary>
    /// <example>8192</example>
    public int SizeBytes { get; init; }

    /// <summary>Gets inspectable state-bag entries.</summary>
    public IReadOnlyList<AgentSessionEntry> Entries { get; init; } = [];
}
