namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports the active agent definition's realtime policy, not provider or session health.</summary>
public sealed record RealtimeCapabilitiesResponse
{
    /// <summary>Gets whether the agent is configured for realtime voice.</summary>
    /// <example>true</example>
    public required bool VoiceEnabled { get; init; }

    /// <summary>Gets whether the realtime definition enables document knowledge tools.</summary>
    /// <example>true</example>
    public required bool KnowledgeToolsEnabled { get; init; }

    /// <summary>Gets whether the realtime definition allows opt-in text retention.</summary>
    /// <example>false</example>
    public required bool RetentionAllowed { get; init; }
}
