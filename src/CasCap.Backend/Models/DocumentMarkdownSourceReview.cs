using System.Text.Json;

namespace CasCap.Models;

/// <summary>Binds explicit source-role and structural decisions to exact normalized pages, without approving indexing.</summary>
internal sealed record DocumentMarkdownSourceReview(
    int Version,
    string ReviewId,
    string MarkdownSha256,
    IReadOnlyList<DocumentMarkdownExclusion> Exclusions,
    IReadOnlyList<DocumentMarkdownHeadingReview> Headings,
    IReadOnlyList<DocumentMarkdownLayoutTableReview> LayoutTables)
{
    /// <summary>Gets the deterministic audit identity, independent of annotation ordering.</summary>
    public string Id => DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(new
    {
        Version,
        ReviewId,
        MarkdownSha256,
        Exclusions = Exclusions.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset)
            .ThenBy(item => item.EndOffset).ToArray(),
        Headings = Headings.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset)
            .ThenBy(item => item.EndOffset).ToArray(),
        LayoutTables = LayoutTables.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset)
            .ThenBy(item => item.EndOffset).ToArray(),
    }));
}
