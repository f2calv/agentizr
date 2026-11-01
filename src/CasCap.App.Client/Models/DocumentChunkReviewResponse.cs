namespace CasCap.Client.Models;

/// <summary>A saved selection revision; never semantic approval or indexing eligibility.</summary>
public sealed record DocumentChunkReviewResponse
{
    /// <summary>Gets the append-only review revision within the exact snapshot.</summary>
    /// <example>1</example>
    public required long Revision { get; init; }

    /// <summary>Gets excluded candidate ordinals and separate reviewer notes.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<DocumentChunkExclusionRequest> Exclusions { get; init; }

    /// <summary>Gets the database-authoritative recording time.</summary>
    /// <example>2026-01-01T00:00:00Z</example>
    public required DateTimeOffset RecordedAtUtc { get; init; }

    /// <summary>Gets the fixed denied indexing approval; classification, safety and provenance gates remain separate.</summary>
    /// <example>false</example>
    public bool IndexingApproved => false;
}
