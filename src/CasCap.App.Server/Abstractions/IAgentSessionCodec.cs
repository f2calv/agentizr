namespace CasCap.Abstractions;

/// <summary>Inspects and transforms Agent Framework session payloads inside the runtime boundary.</summary>
public interface IAgentSessionCodec
{
    /// <summary>Inspects serialized session state without exposing its raw payload.</summary>
    public ValueTask<AgentSessionInspection> InspectAsync(
        AgentDefinition definition,
        string? providerApiKey,
        string sessionStateJson,
        CancellationToken cancellationToken);

    /// <summary>Compacts serialized session state while preserving its framework-defined shape.</summary>
    public ValueTask<AgentSessionCompactionResult> CompactAsync(
        AgentDefinition definition,
        string? providerApiKey,
        string sessionStateJson,
        int retainMessageCount,
        CancellationToken cancellationToken);
}
