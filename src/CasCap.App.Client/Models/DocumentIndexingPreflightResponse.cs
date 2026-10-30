namespace CasCap.Client.Models;

/// <summary>Reports a read-only indexing input check; it never grants paid-operation or publication approval.</summary>
public sealed record DocumentIndexingPreflightResponse
{
    /// <summary>Gets whether current review, trusted policy and input bounds pass.</summary>
    /// <example>true</example>
    public bool Eligible { get; init; }

    /// <summary>Gets whether indexing is authorized; preflight never authorizes it.</summary>
    /// <example>false</example>
    public bool IndexingApproved => false;

    /// <summary>Gets explicit unresolved eligibility gates.</summary>
    /// <example>["No trusted indexing policy is configured for this collection."]</example>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Gets the exact input manifest identity, present only for eligible inputs.</summary>
    /// <example>null</example>
    public string? ManifestSha256 { get; init; }

    /// <summary>Gets the observed publication predecessor for explicit acceptance; it is not proof of current search eligibility.</summary>
    /// <example>null</example>
    public string? ObservedActiveJobId { get; init; }

    /// <summary>Gets the saved selection revision observed atomically with the assessment.</summary>
    /// <example>1</example>
    public long? ChunkReviewRevision { get; init; }

    /// <summary>Gets the latest assessment revision.</summary>
    /// <example>1</example>
    public long? AssessmentRevision { get; init; }

    /// <summary>Gets the included candidate count; exclusions are never embedded.</summary>
    /// <example>24</example>
    public int IncludedChunkCount { get; init; }

    /// <summary>Gets the total complete-input tokens using the compatible captured tokenizer.</summary>
    /// <example>6000</example>
    public long TotalInputTokens { get; init; }
}
