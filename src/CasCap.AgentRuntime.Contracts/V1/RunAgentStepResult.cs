namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports diagnostics for one delegated agent step.</summary>
public sealed record RunAgentStepResult
{
    /// <summary>Gets total delegated execution time in milliseconds.</summary>
    /// <example>850</example>
    public double ElapsedMilliseconds { get; init; }

    /// <summary>Gets provider token usage when reported.</summary>
    public RunAgentUsage? Usage { get; init; }

    /// <summary>Gets tool calls made by the delegated agent.</summary>
    public IReadOnlyList<RunAgentToolCall> ToolCalls { get; init; } = [];

    /// <summary>Gets non-sensitive diagnostic properties reported by the provider or host.</summary>
    public IReadOnlyDictionary<string, JsonElement> AdditionalProperties { get; init; } =
        new Dictionary<string, JsonElement>();
}
