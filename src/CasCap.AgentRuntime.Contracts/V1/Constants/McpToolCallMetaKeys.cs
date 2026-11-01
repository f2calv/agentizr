namespace CasCap.AgentRuntime.Contracts.V1.Constants;

/// <summary>Stable <c>_meta</c> keys the Agent Runtime attaches to every remote MCP tool call.</summary>
/// <remarks>
/// The values are asserted by the calling runtime and are informational: a server may show them to a
/// person, for example in an approval prompt, but must not base an authorization decision on them.
/// </remarks>
public static class McpToolCallMetaKeys
{
    /// <summary>Display name of the agent whose model requested the tool call.</summary>
    public const string AgentName = "agentizr/agent-name";
}
