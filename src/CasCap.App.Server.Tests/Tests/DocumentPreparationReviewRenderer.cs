#if DEBUG
using System.Net;

namespace CasCap.Tests;

/// <summary>Renders review metadata separately from transcription; usage stays in the receipt/export.</summary>
internal static partial class DocumentPreparationReviewRenderer
{
    /// <summary>Reads only encoded transcription elements from this export format, never drafts or review annotations.</summary>
    public static IReadOnlyDictionary<int, DocumentMarkdownPage[]> ReadPages(string html)
    {
        var pages = new Dictionary<int, List<DocumentMarkdownPage>>();
        int? source = null;
        var pageNumber = 0;
        foreach (Match match in ExportElement().Matches(html))
        {
            if (match.Groups["source"].Success)
            {
                if (source is not null)
                    throw new InvalidDataException("A review page has no paired transcription.");
                source = int.Parse(match.Groups["source"].Value, System.Globalization.CultureInfo.InvariantCulture);
                pageNumber = int.Parse(match.Groups["page"].Value, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            if (source is null)
                throw new InvalidDataException("A transcription has no original source/page heading.");
            if (!pages.TryGetValue(source.Value, out var selected))
                pages.Add(source.Value, selected = []);
            if (selected.Count != 0 && selected[^1].PageNumber >= pageNumber)
                throw new InvalidDataException("Export pages must be unique and increasing within each source.");
            selected.Add(new(pageNumber, WebUtility.HtmlDecode(match.Groups["markdown"].Value)));
            source = null;
        }
        if (source is not null || pages.Count == 0)
            throw new InvalidDataException("The review export has no complete transcription selection.");
        return pages.ToDictionary(item => item.Key, item => item.Value.ToArray());
    }

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
        + (result.Review is not { } review ? string.Empty
            : "<aside class='review-metadata'><h4>Reviewed source exclusions (not embedding text)</h4><ul>"
                + string.Concat(review.Exclusions.Select(item =>
                    $"<li>Physical page {item.PageNumber}, Markdown [{item.StartOffset}, {item.EndOffset}): "
                    + $"{WebUtility.HtmlEncode(item.Role)}; {WebUtility.HtmlEncode(item.Reason)}</li>"))
                + "</ul></aside>"
                + "<aside class='review-metadata'><h4>Reviewed structural decisions (not source transcription)</h4><ul>"
                + string.Concat(review.Headings.Select(item => StructuralDecision(item.PageNumber, item.StartOffset, item.EndOffset,
                    $"heading level {item.Level}", item.Reason)))
                + string.Concat(review.LayoutTables.Select(item => StructuralDecision(item.PageNumber, item.StartOffset, item.EndOffset,
                    $"independent layout entries; {item.ReadingOrder}", item.Reason)))
                + "</ul></aside>")
        + string.Concat(result.Chunks.Select(chunk =>
            $"<article><h4>Chunk {chunk.Ordinal + 1}</h4>"
            + $"<p>Physical pages: {string.Join(", ", chunk.SourceSpans.Select(span => span.PageNumber).Distinct())}; "
            + $"tokens including headings: {chunk.TokenCount}; fragmented: {chunk.IsFragmented}</p>"
            + $"<pre class='embedding-preview'>{WebUtility.HtmlEncode(chunk.EmbeddingText)}</pre></article>"))
        + "</details>";

    private static string StructuralDecision(int page, int start, int end, string decision, string reason) =>
        $"<li>Physical page {page}, Markdown [{start}, {end}): {WebUtility.HtmlEncode(decision)}; {WebUtility.HtmlEncode(reason)}</li>";

    private static string Notes(string[] notes) =>
        string.Concat(notes.Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => $"<li>{WebUtility.HtmlEncode(note)}</li>"));

    [GeneratedRegex(@"<h2>Source (?<source>[1-9][0-9]*), original page (?<page>[1-9][0-9]*)</h2>|<pre class='transcription'>(?<markdown>[^<]*)</pre>",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ExportElement();
}
#endif
