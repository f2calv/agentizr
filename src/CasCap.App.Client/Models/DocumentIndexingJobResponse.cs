namespace CasCap.Client.Models;

/// <summary>Payload-free durable indexing progress; publication is not independent model retrieval permission.</summary>
public sealed record DocumentIndexingJobResponse
{
    /// <summary>Gets the durable indexing generation identity.</summary>
    /// <example>example-indexing-job</example>
    public required string Id { get; init; }

    /// <summary>Gets the persisted lifecycle state.</summary>
    /// <example>Queued</example>
    public required string State { get; init; }

    /// <summary>Gets whether current policy/source/review bindings still permit this generation.</summary>
    /// <example>true</example>
    public bool IsCurrent { get; init; }

    /// <summary>Gets whether the published pointer selects this still-eligible generation.</summary>
    /// <example>false</example>
    public bool IsActive { get; init; }

    /// <summary>Gets the number of committed staged vectors.</summary>
    /// <example>0</example>
    public int VectorCount { get; init; }

    /// <summary>Gets the number of committed paid submission intents.</summary>
    /// <example>0</example>
    public int SubmittedRequests { get; init; }

    /// <summary>Gets native received input-token usage; absence of a receipt is not evidence of no charge.</summary>
    /// <example>0</example>
    public long ReceivedInputTokens { get; init; }
}
