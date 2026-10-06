namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Describes one entry in serialized agent session state.</summary>
public sealed record AgentSessionEntry
{
    /// <summary>Gets the session state-bag key.</summary>
    /// <example>Microsoft.Agents.AI.ChatHistory</example>
    public required string Key { get; init; }

    /// <summary>Gets the serialized entry size in bytes.</summary>
    /// <example>4096</example>
    public int ByteSize { get; init; }

    /// <summary>Gets the total message count represented by the entry.</summary>
    /// <example>12</example>
    public int MessageCount { get; init; }

    /// <summary>Gets the user-message count represented by the entry.</summary>
    /// <example>6</example>
    public int UserMessageCount { get; init; }

    /// <summary>Gets the assistant-message count represented by the entry.</summary>
    /// <example>6</example>
    public int AssistantMessageCount { get; init; }
}
