namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>An estimated provider cost for debugging, computed from operator-configured token prices.</summary>
/// <remarks>Not a bill: prices are configuration, and provider rounding, discounts and taxes are not applied.</remarks>
public sealed record RealtimeCallCost
{
    /// <summary>Gets the estimated amount.</summary>
    /// <example>0.45</example>
    public required decimal Amount { get; init; }

    /// <summary>Gets the ISO 4217 currency code of the configured prices.</summary>
    /// <example>EUR</example>
    public required string Currency { get; init; }
}
