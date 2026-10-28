namespace CasCap.Models;

/// <summary>A block-scoped half-open UTF-16 range in the supplied page Markdown, not PDF coordinates.</summary>
internal sealed record DocumentMarkdownSourceSpan(int PageNumber, int StartOffset, int EndOffset, int BlockOrdinal);
