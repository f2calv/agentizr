namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Returns the output and resolved definition version for one agent turn.</summary>
public sealed record RunAgentResponse
{
    /// <summary>Gets the caller-owned session identifier.</summary>
    /// <example>conversation-42</example>
    public required string SessionId { get; init; }

    /// <summary>Gets the agent output text.</summary>
    /// <example>All monitored services are healthy.</example>
    public required string OutputText { get; init; }

    /// <summary>Gets the version of the tenant agent definition used for this turn.</summary>
    /// <example>1</example>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets the provider model used for the turn.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets the provider finish reason when reported.</summary>
    public string? FinishReason { get; init; }

    /// <summary>Gets total runtime execution time in milliseconds.</summary>
    public double ElapsedMilliseconds { get; init; }

    /// <summary>Gets time to first output token in milliseconds when measured.</summary>
    public double? TimeToFirstTokenMilliseconds { get; init; }

    /// <summary>Gets provider token usage when reported.</summary>
    public RunAgentUsage? Usage { get; init; }

    /// <summary>Gets tool calls in invocation order without argument payloads.</summary>
    public IReadOnlyList<RunAgentToolCall> ToolCalls { get; init; } = [];

    /// <summary>Gets binary attachments produced by tools or delegated agents.</summary>
    public IReadOnlyList<RunAgentAttachment> Attachments { get; init; } = [];

    /// <summary>Gets structured delegation and compaction events observed during the turn.</summary>
    public IReadOnlyList<RunAgentEvent> Events { get; init; } = [];

    /// <summary>Gets resulting session status when persistence was enabled.</summary>
    public AgentSessionInfoResponse? Session { get; init; }

    /// <summary>Gets non-sensitive diagnostic properties reported by the provider or host.</summary>
    public IReadOnlyDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>();
}
