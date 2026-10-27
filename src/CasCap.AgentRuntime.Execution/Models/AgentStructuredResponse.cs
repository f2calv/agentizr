namespace CasCap.AgentRuntime.Execution.Models;

/// <summary>Explicit provider-response projection observed before structured validation; not a fidelity approval.</summary>
public sealed record AgentStructuredResponse
{
    /// <summary>Gets the returned text, including incomplete or malformed JSON.</summary>
    public required string Text { get; init; }

    /// <summary>Gets whether the authoritative Responses result completed without a provider error.</summary>
    public required bool IsComplete { get; init; }

    /// <summary>Gets the reported model identifier, which may omit the deployment revision.</summary>
    public string? ModelId { get; init; }

    /// <summary>Gets the provider receipt identifier.</summary>
    public string? ResponseId { get; init; }

    /// <summary>Gets reported usage; missing counters are unknown rather than zero.</summary>
    public UsageDetails? Usage { get; init; }
}
