namespace CasCap.Models;

/// <summary>Body provenance is separate from the contextual embedding projection; no geometry or search activation is implied.</summary>
internal sealed record DocumentMarkdownChunk(
    int Ordinal,
    IReadOnlyList<DocumentMarkdownHeading> HeadingPath,
    string Body,
    string EmbeddingText,
    int TokenCount,
    IReadOnlyList<DocumentMarkdownSourceSpan> SourceSpans,
    bool IsFragmented);
