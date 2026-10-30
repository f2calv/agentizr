using System.Text.Json;

namespace CasCap.Client.Models;

/// <summary>Reads the supported subset of a versioned projection for the browser workbench.</summary>
internal sealed record DocumentReviewProjectionView
{
    /// <summary>Gets the complete-input chunk budgets.</summary>
    public required ProfileView Profile { get; init; }

    /// <summary>Gets contextual embedding candidates, never active search records.</summary>
    public required IReadOnlyList<ChunkView> Chunks { get; init; }

    /// <summary>Gets the original structural decisions, if supplied by the projection.</summary>
    public JsonElement? Review { get; init; }

    /// <summary>Reads and validates the supported display shape, rejecting missing or inconsistent metadata.</summary>
    public static DocumentReviewProjectionView Read(JsonElement projection)
    {
        var view = projection.Deserialize<DocumentReviewProjectionView>(JsonSerializerOptions.Web)
            ?? throw new JsonException("The review projection is missing.");
        if (view.Profile is not { Strategy: "markdown-headings", MaximumTokensPerChunk: >= 8 and <= 8_192, OverlapTokens: >= 0 } profile
            || profile.TargetTokensPerChunk < 8 || profile.TargetTokensPerChunk > profile.MaximumTokensPerChunk
            || profile.OverlapTokens >= profile.MaximumTokensPerChunk || view.Chunks is null
            || view.Chunks.Any(chunk => chunk is null || chunk.Ordinal < 0 || chunk.TokenCount < 1
                || chunk.TokenCount > profile.MaximumTokensPerChunk || chunk.EmbeddingText is null
                || chunk.SourceSpans.ValueKind != JsonValueKind.Array)
            || view.Chunks.Select(chunk => chunk.Ordinal).Distinct().Count() != view.Chunks.Count)
            throw new JsonException("The review projection has an unsupported shape.");
        return view;
    }

    /// <summary>The supported profile fields; additional server metadata remains outside this display model.</summary>
    public sealed record ProfileView
    {
        /// <summary>Gets the projection strategy.</summary>
        public required string Strategy { get; init; }

        /// <summary>Gets the packing target.</summary>
        public required int TargetTokensPerChunk { get; init; }

        /// <summary>Gets the hard complete-input token ceiling.</summary>
        public required int MaximumTokensPerChunk { get; init; }

        /// <summary>Gets paragraph suffix overlap.</summary>
        public required int OverlapTokens { get; init; }
    }

    /// <summary>One safely displayed contextual candidate with its original Markdown provenance.</summary>
    public sealed record ChunkView
    {
        /// <summary>Gets the zero-based candidate ordinal.</summary>
        public required int Ordinal { get; init; }

        /// <summary>Gets the complete contextual token count.</summary>
        public required int TokenCount { get; init; }

        /// <summary>Gets heading ancestry followed by the candidate body.</summary>
        public required string EmbeddingText { get; init; }

        /// <summary>Gets physical-page/Markdown spans; these are not PDF geometry.</summary>
        public required JsonElement SourceSpans { get; init; }
    }
}
