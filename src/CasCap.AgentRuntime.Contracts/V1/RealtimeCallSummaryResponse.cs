namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>How a realtime voice call ended and what it used.</summary>
/// <remarks>Returned when ending a call, and for a short while after the runtime ended it on its own.</remarks>
public sealed record RealtimeCallSummaryResponse
{
    /// <summary>Gets why the call ended.</summary>
    /// <example>MaximumDuration</example>
    [JsonConverter(typeof(JsonStringEnumConverter<RealtimeCallEndReason>))]
    public required RealtimeCallEndReason EndReason { get; init; }

    /// <summary>Gets how long the runtime controlled the call, in whole seconds.</summary>
    /// <example>300</example>
    public required int DurationSeconds { get; init; }

    /// <summary>Gets the token usage the provider reported.</summary>
    public required RealtimeCallUsage Usage { get; init; }

    /// <summary>Gets the estimated cost, or <see langword="null"/> when no prices are configured for the deployment.</summary>
    public RealtimeCallCost? EstimatedCost { get; init; }
}
