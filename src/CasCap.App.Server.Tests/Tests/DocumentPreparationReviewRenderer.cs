#if DEBUG
using System.Net;

namespace CasCap.Tests;

/// <summary>Renders review metadata separately from transcription; usage stays in the receipt/export.</summary>
internal static class DocumentPreparationReviewRenderer
{
    public static string Render(DocumentPreparationCandidate candidate)
    {
        var changes = Notes(candidate.Changes);
        var uncertainties = Notes(candidate.Uncertainties);
        return $"<pre class='transcription'>{WebUtility.HtmlEncode(candidate.Markdown)}</pre>"
            + (uncertainties.Length == 0 ? string.Empty
                : $"<aside class='review-metadata'><h4>Page Uncertainties</h4><ul>{uncertainties}</ul></aside>")
            + (changes.Length == 0 ? string.Empty
                : $"<details class='review-metadata'><summary>Review changes (not document content)</summary><ul>{changes}</ul></details>");
    }

    /// <summary>Shows nonpersistent heading-aware chunks without implying fidelity approval, embeddings or searchable activation.</summary>
    public static string RenderChunks(DocumentMarkdownChunkingResult result) =>
        "<details class='chunk-preview'><summary>Review-only markdown-headings chunks</summary>"
        + string.Concat(result.Chunks.Select(chunk =>
            $"<article><h4>Chunk {chunk.Ordinal + 1}</h4>"
            + $"<p>Physical pages: {string.Join(", ", chunk.SourceSpans.Select(span => span.PageNumber).Distinct())}; "
            + $"tokens including headings: {chunk.TokenCount}; fragmented: {chunk.IsFragmented}</p>"
            + $"<pre class='embedding-preview'>{WebUtility.HtmlEncode(chunk.EmbeddingText)}</pre></article>"))
        + "</details>";

    private static string Notes(string[] notes) =>
        string.Concat(notes.Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => $"<li>{WebUtility.HtmlEncode(note)}</li>"));
}
#endif
