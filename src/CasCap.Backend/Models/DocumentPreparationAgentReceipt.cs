namespace CasCap.Models;

/// <summary>Bounded response projection captured even when the provider output fails local validation.</summary>
internal sealed record DocumentPreparationAgentReceipt
{
    /// <summary>Gets the explicit response projection; text is omitted only when FailureCode identifies a size rejection.</summary>
    public required AgentStructuredResponse Response { get; init; }

    /// <summary>Gets a content-free explicit rejection reason, not an exception or service response body.</summary>
    public string? FailureCode { get; init; }
}
