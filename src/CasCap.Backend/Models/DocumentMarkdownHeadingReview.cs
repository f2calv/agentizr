namespace CasCap.Models;

/// <summary>Assigns a reviewed level to an exact heading or single-line paragraph without rewriting its source.</summary>
internal sealed record DocumentMarkdownHeadingReview(int PageNumber, int StartOffset, int EndOffset, int Level, string Reason);
