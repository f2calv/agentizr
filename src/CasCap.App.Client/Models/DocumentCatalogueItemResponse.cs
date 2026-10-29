namespace CasCap.Client.Models;

/// <summary>Describes an admitted source and its current non-ignored job without exposing storage locations or plans.</summary>
public sealed record DocumentCatalogueItemResponse
{
    /// <summary>Gets the opaque source identity.</summary>
    /// <example>example-document</example>
    public required string DocumentId { get; init; }

    /// <summary>Gets the tenant-local collection.</summary>
    /// <example>example-collection</example>
    public required string CollectionId { get; init; }

    /// <summary>Gets the original source's final path segment, for display only.</summary>
    /// <example>manual.pdf</example>
    public required string FileName { get; init; }

    /// <summary>Gets durable source admission state, not search readiness.</summary>
    /// <example>Queued</example>
    public required string SourceState { get; init; }

    /// <summary>Gets the current source's job identity, if present.</summary>
    /// <example>example-job</example>
    public string? JobId { get; init; }

    /// <summary>Gets the current job state, if present.</summary>
    /// <example>ReviewRequired</example>
    public string? JobState { get; init; }

    /// <summary>Gets whether the observed job can currently be previewed; the review API rechecks this independently.</summary>
    /// <example>true</example>
    public bool CanReview { get; init; }

    /// <summary>Gets one current-source immutable review identity, not necessarily the newest, if present.</summary>
    /// <example>null</example>
    public string? StoredGenerationId { get; init; }
}
