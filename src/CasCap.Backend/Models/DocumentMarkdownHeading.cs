namespace CasCap.Models;

/// <summary>A source-derived heading and its distinct provenance, without invented missing ancestor levels.</summary>
internal sealed record DocumentMarkdownHeading(
    int Level,
    string Markdown,
    IReadOnlyList<DocumentMarkdownSourceSpan> SourceSpans);
