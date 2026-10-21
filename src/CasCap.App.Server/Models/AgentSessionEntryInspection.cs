namespace CasCap.Models;

/// <summary>Runtime-owned summary of one Agent Framework session state-bag entry.</summary>
public sealed record AgentSessionEntryInspection
{
    /// <summary>Gets the state-bag key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the serialized entry size in bytes.</summary>
    public int ByteSize { get; init; }

    /// <summary>Gets the total represented message count.</summary>
    public int MessageCount { get; init; }

    /// <summary>Gets the represented user-message count.</summary>
    public int UserMessageCount { get; init; }

    /// <summary>Gets the represented assistant-message count.</summary>
    public int AssistantMessageCount { get; init; }
}
