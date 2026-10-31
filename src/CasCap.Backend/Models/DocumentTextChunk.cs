namespace CasCap.Models;

/// <summary>A zero-based ordered chunk with a one-based PDF page and original extracted UTF-16 offsets.</summary>
internal sealed record DocumentTextChunk(
    int Ordinal,
    int PageNumber,
    int StartOffset,
    int EndOffset,
    int TokenCount,
    string Text);
