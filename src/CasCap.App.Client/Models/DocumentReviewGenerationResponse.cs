using System.Text.Json;

namespace CasCap.Client.Models;

/// <summary>An authorized current-source preview or immutable review-required generation, never search approval.</summary>
public sealed record DocumentReviewGenerationResponse
{
    /// <summary>Gets the immutable identity, or null for a non-persisted preview.</summary>
    /// <example>null</example>
    public string? GenerationId { get; init; }

    /// <summary>Gets the fixed review-required state.</summary>
    /// <example>ReviewRequired</example>
    public required string State { get; init; }

    /// <summary>Gets the current source sequence required when storing a generation.</summary>
    /// <example>0000000000000001</example>
    public required string Sequencer { get; init; }

    /// <summary>Gets the current source ETag required when storing a generation.</summary>
    /// <example>"example-etag"</example>
    public required string ETag { get; init; }

    /// <summary>Gets the digest of the original PDF.</summary>
    /// <example>0000000000000000000000000000000000000000000000000000000000000000</example>
    public required string SourceSha256 { get; init; }

    /// <summary>Gets normalized pages and their separate preparation notes.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<DocumentReviewPageResponse> Pages { get; init; }

    /// <summary>Gets the versioned heading-aware projection, including its profile, review, chunks and Markdown spans.</summary>
    /// <remarks>Spans are half-open UTF-16 Markdown offsets, not PDF geometry. No native response, usage or credential is exposed.</remarks>
    /// <example>{}</example>
    public required JsonElement Projection { get; init; }

    /// <summary>Gets the latest selection review, separate from the immutable projection and never approval.</summary>
    /// <example>null</example>
    public DocumentChunkReviewResponse? ChunkReview { get; init; }
}
