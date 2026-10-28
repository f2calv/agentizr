using System.Text.Json;

namespace CasCap.Models;

/// <summary>Binds explicit source-role decisions to an exact normalized page selection, without approving indexing.</summary>
internal sealed record DocumentMarkdownSourceReview(
    int Version,
    string ReviewId,
    string MarkdownSha256,
    IReadOnlyList<DocumentMarkdownExclusion> Exclusions)
{
    /// <summary>Gets the deterministic audit identity, independent of annotation ordering.</summary>
    public string Id => DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(new
    {
        Version,
        ReviewId,
        MarkdownSha256,
        Exclusions = Exclusions.OrderBy(item => item.PageNumber).ThenBy(item => item.StartOffset)
            .ThenBy(item => item.EndOffset).ToArray(),
    }));
}
