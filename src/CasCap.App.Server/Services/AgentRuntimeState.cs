using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>Process-local backing state for bootstrap session and override stores.</summary>
internal sealed class AgentRuntimeState
{
    /// <summary>Gets serialized sessions keyed by tenant, agent, and session.</summary>
    public ConcurrentDictionary<string, string> Sessions { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets overrides keyed by tenant, agent, and session.</summary>
    public ConcurrentDictionary<string, AgentOverrideState> Overrides { get; } = new(StringComparer.Ordinal);
}
