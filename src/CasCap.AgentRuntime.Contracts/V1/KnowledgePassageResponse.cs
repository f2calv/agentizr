namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Contains untrusted source data with a protected citation identity, never an instruction or signed Blob URL.</summary>
public sealed record KnowledgePassageResponse
{
    /// <summary>Gets the opaque published indexing generation identity used by the source-preview route.</summary>
    /// <example>example-index</example>
    public required string IndexJobId { get; init; }

    /// <summary>Gets the tenant-local document identity.</summary>
    /// <example>example-document</example>
    public required string DocumentId { get; init; }

    /// <summary>Gets the immutable reviewed chunk ordinal.</summary>
    /// <example>1</example>
    public required int ChunkOrdinal { get; init; }

    /// <summary>Gets the reviewed source content hash.</summary>
    /// <example>0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef</example>
    public required string SourceSha256 { get; init; }

    /// <summary>Gets the reviewed document CEFR level.</summary>
    /// <example>A1</example>
    public required string CefrLevel { get; init; }

    /// <summary>Gets the complete passage body, bounded to 8,000 UTF-16 code units.</summary>
    /// <example>Synthetic example passage.</example>
    public required string Text { get; init; }

    /// <summary>Gets up to 64 source block spans.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<KnowledgeSourceSpan> SourceSpans { get; init; }
}
