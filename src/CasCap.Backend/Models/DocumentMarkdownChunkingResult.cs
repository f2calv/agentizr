using System.Text.Json;

namespace CasCap.Models;

/// <summary>A bounded review projection tied to exact page Markdown; not approval or a PDF source digest.</summary>
internal sealed record DocumentMarkdownChunkingResult(
    DocumentMarkdownChunkingProfile Profile,
    string MarkdownSha256,
    IReadOnlyList<DocumentMarkdownChunk> Chunks,
    DocumentMarkdownSourceReview? Review = null)
{
    /// <summary>Gets a projection identity fenced by source, algorithm/settings and the exact review decisions.</summary>
    public string Id => DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(new
    {
        ProfileId = Profile.Id,
        MarkdownSha256,
        ReviewId = Review?.Id,
    }));
}
