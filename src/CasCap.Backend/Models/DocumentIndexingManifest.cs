#if DEBUG
namespace CasCap.Models;

/// <summary>Captures exact review, trusted policy/profile and embedding inputs; not a durable job or billing authorization.</summary>
internal sealed record DocumentIndexingManifest
{
    public int Version => 1;

    public string InputVersion => "reviewed-heading-aware-v1";

    public required string TenantId { get; init; }

    public required string CollectionId { get; init; }

    public required string DocumentId { get; init; }

    public required string JobId { get; init; }

    public required string Sequencer { get; init; }

    public required string ETag { get; init; }

    public required string SourceSha256 { get; init; }

    public required string GenerationId { get; init; }

    public required long ChunkReviewRevision { get; init; }

    public required long AssessmentRevision { get; init; }

    public required DocumentIndexingCollectionConfig Policy { get; init; }

    public required DocumentEmbeddingProfileConfig Profile { get; init; }

    public required IReadOnlyList<DocumentIndexingInput> Inputs { get; init; }
}
#endif
