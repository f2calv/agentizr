namespace CasCap.Models;

/// <summary>Records a reviewed whole-block exclusion using half-open UTF-16 offsets in immutable page Markdown.</summary>
internal sealed record DocumentMarkdownExclusion(
    int PageNumber,
    int StartOffset,
    int EndOffset,
    string Role,
    string Reason);
