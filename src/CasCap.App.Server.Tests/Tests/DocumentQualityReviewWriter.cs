#if DEBUG
using System.Text;
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Writes explicitly requested private review artifacts; source text is never interpreted as HTML or Markdown.</summary>
internal static class DocumentQualityReviewWriter
{
    /// <summary>One PDF page in the measured baseline and exploratory spatial reading order.</summary>
    internal sealed record Page(int Number, string ContentOrderText, string SpatialText, DocumentLayoutReview.Result? Layout = null);

    /// <summary>Writes an original PDF, escaped side-by-side HTML and a fenced Markdown review without overwriting files.</summary>
    public static async Task WriteSourceAsync(
        string directory,
        string stem,
        ReadOnlyMemory<byte> original,
        IReadOnlyList<Page> pages,
        PdfPreparationResult prepared,
        CancellationToken cancellationToken
    )
    {
        ValidateStem(stem);
        var hasLayout = pages.Any(page => page.Layout is not null);
        var html = new StringBuilder("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-src 'self' file:; base-uri 'none'; form-action 'none'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Private PDF chunk review</title>
            <style>
            body { margin: 0; font-family: system-ui, sans-serif; color: #18212b; background: #f6f8fa; }
            header { padding: 1rem; border-bottom: 1px solid #ccd3da; }
            select { margin-right: 1rem; padding: .3rem; }
            main { display: grid; grid-template-columns: 1fr 1fr; height: 78vh; }
            iframe { width: 100%; height: 100%; border: 0; }
            #extracted { padding: 1rem; overflow: auto; }
            pre { white-space: pre-wrap; overflow-wrap: anywhere; background: white; padding: 1rem; border: 1px solid #ccd3da; }
            .chunk { border-left: 4px solid #4361a8; padding-left: .7rem; }
            [hidden] { display: none !important; }
            @media (max-width: 850px) { main { grid-template-columns: 1fr; height: auto; } iframe { height: 65vh; } }
            </style></head><body><header><h1>Private PDF chunk review</h1>
            <p>This review retains the original baseline alongside experimental layout candidates, not production chunking profiles.
            When available, the section layout candidate is selected by default.
            Original content-order, spatial and token-chunk views retain baseline extraction defects for comparison.</p>
            <p>Section review: check heading/paragraph, warning/procedure and table/header associations.
            Entry review: check headword, forms, meaning and examples, including continuation across pages.</p>
            <p>Compare against the original, not just the other extraction. Record independently reviewed passage/order expectations.</p>
            """);
        html.Append("<p><a href=\"index.html\">Review index</a> | <a target=\"_blank\" rel=\"noopener\" href=\"")
            .Append(stem).Append(".pdf\">Open original PDF</a> | <a href=\"")
            .Append(stem).Append(".md\">Original baseline Markdown</a>");
        if (hasLayout)
            html.Append(" | <a href=\"").Append(stem).Append(".sections.md\">Refined section Markdown (")
                .Append(DocumentLayoutReview.Version).Append(")</a>");
        html.Append("</p><p>Source SHA-256: <code>")
            .Append(WebUtility.HtmlEncode(prepared.SourceSha256)).Append("</code></p><p>Baseline algorithm/profile: <code>")
            .Append(WebUtility.HtmlEncode(prepared.Profile.Algorithm)).Append(" / ")
            .Append(WebUtility.HtmlEncode(prepared.Profile.Id)).Append("</code>; token limit: ")
            .Append(prepared.Profile.MaximumTokensPerChunk).Append("; overlap: ")
            .Append(prepared.Profile.OverlapTokens).Append("</p><label>PDF page <select id=\"page\">");
        foreach (var page in pages)
            html.Append("<option value=\"").Append(page.Number).Append("\">").Append(page.Number).Append("</option>");
        html.Append("""
            </select></label><label>View <select id="view">
            """);
        html.Append("<option value=\"content\"").Append(hasLayout ? "" : " selected")
            .Append(">Original content-order text (baseline)</option>");
        html.Append("""
            <option value="spatial">Original spatial reading order (baseline, exploratory)</option>
            """);
        html.Append("<option value=\"sections\"").Append(hasLayout ? " selected" : "")
            .Append(">Section layout candidate</option>");
        html.Append("""
            <option value="entries">Entry-row association candidate</option>
            <option value="margins">Excluded margin candidates</option>
            <option value="chunks">Original token chunks (baseline)</option>
            </select></label></header><main><iframe id="pdf" title="Original PDF"></iframe><section id="extracted">
            """);
        var markdown = new StringBuilder("# Private PDF baseline review\n\n")
            .Append("Original PDF: [open](").Append(stem).Append(".pdf)\n\n")
            .Append("Source SHA-256: `").Append(prepared.SourceSha256).Append("`\n\n")
            .Append("Algorithm/profile: `").Append(prepared.Profile.Algorithm).Append(" / ").Append(prepared.Profile.Id)
            .Append("`; token limit: ").Append(prepared.Profile.MaximumTokensPerChunk)
            .Append("; overlap: ").Append(prepared.Profile.OverlapTokens).Append("\n\n")
            .Append("Baseline only; spatial text is exploratory, not a semantic profile or a Markdown conversion guarantee.\n\n")
            .Append("Text views add blank lines around uppercase numbered heading candidates for review only. ")
            .Append("Chunk text and offsets are unchanged; footer and reading-order defects remain visible.\n\n");
        foreach (var page in pages)
        {
            html.Append("<article data-page=\"").Append(page.Number).Append("\"><h2>PDF page ").Append(page.Number).Append("</h2>");
            html.Append("<p>Original baseline views retain extraction defects and unchanged chunk text/offsets. ")
                .Append("Layout refinements appear only in the candidate views and their separate Markdown exports.</p>");
            AppendView(html, "content", AddHeadingSpacing(page.ContentOrderText));
            AppendView(html, "spatial", AddHeadingSpacing(page.SpatialText));
            if (page.Layout is not null)
            {
                html.Append("<p>").Append(DocumentLayoutReview.Version)
                    .Append(" is experimental. Section order uses heading bands and baseline rows; entry rows can interleave independent columns. ")
                    .Append("Paragraph gaps and headings are candidates. Encoded capitalization is unchanged; bold style is retained. ")
                    .Append("Repeated page-margin rows are excluded only from candidates and remain separately inspectable. ")
                    .Append("Figures, table semantics and whole vocabulary-entry continuations are not reconstructed.</p>")
                    .Append("<p><a href=\"").Append(stem).Append(".sections.md\">Section Markdown</a> | <a href=\"")
                    .Append(stem).Append(".entries.md\">Entry-row Markdown</a> | <a href=\"")
                    .Append(stem).Append(".layout.json\">Coordinates and coverage</a></p>");
                AppendView(html, "sections", page.Layout.Sections);
                AppendView(html, "entries", page.Layout.Entries);
                AppendView(html, "margins", page.Layout.ExcludedMargins);
            }
            html.Append("<section data-kind=\"chunks\">");
            markdown.Append("## PDF page ").Append(page.Number).Append("\n\n### Content-order text\n\n");
            AppendFencedText(markdown, AddHeadingSpacing(page.ContentOrderText));
            markdown.Append("\n### Spatial reading order (exploratory)\n\n");
            AppendFencedText(markdown, AddHeadingSpacing(page.SpatialText));
            markdown.Append("\n### Current token chunks\n\n");
            foreach (var chunk in prepared.Chunks.Where(chunk => chunk.PageNumber == page.Number))
            {
                var label = $"Chunk {chunk.Ordinal}: tokens={chunk.TokenCount}, UTF-16 span=[{chunk.StartOffset},{chunk.EndOffset})";
                html.Append("<div class=\"chunk\"><h3>").Append(WebUtility.HtmlEncode(label)).Append("</h3><pre>")
                    .Append(WebUtility.HtmlEncode(chunk.Text)).Append("</pre></div>");
                markdown.Append("#### ").Append(label).Append("\n\n");
                AppendFencedText(markdown, chunk.Text);
            }
            html.Append("</section></article>");
        }
        html.Append("</section></main><script>const original='").Append(stem).Append("""
            .pdf';
            const pageSelector=document.getElementById('page');
            const viewSelector=document.getElementById('view');
            function showPage() {
              for (const panel of document.querySelectorAll('[data-page]'))
                panel.hidden=panel.dataset.page!==pageSelector.value;
              document.getElementById('pdf').src=original+'#page='+pageSelector.value+'&view=FitH';
            }
            function showView() {
              for (const panel of document.querySelectorAll('[data-kind]'))
                panel.hidden=panel.dataset.kind!==viewSelector.value;
            }
            pageSelector.addEventListener('change',showPage);
            viewSelector.addEventListener('change',showView);
            showPage(); showView();
            </script></body></html>
            """);
        await CreateFileAsync(Path.Combine(directory, $"{stem}.pdf"), original, cancellationToken);
        await CreateFileAsync(Path.Combine(directory, $"{stem}.html"), Encoding.UTF8.GetBytes(html.ToString()), cancellationToken);
        await CreateFileAsync(Path.Combine(directory, $"{stem}.md"), Encoding.UTF8.GetBytes(markdown.ToString()), cancellationToken);
        if (pages.Any(page => page.Layout is not null))
        {
            if (pages.Any(page => page.Layout is null))
                throw new InvalidOperationException("A structured review requires layout capture for every page.");
            const string notice = "# " + DocumentLayoutReview.Version + ": experimental candidate\n\nNot a production chunking profile or semantic acceptance. "
                + "Compare with the original PDF. Encoded capitalization is retained; figure semantics and entry continuations are unresolved. "
                + "Repeated margin candidates are excluded here but preserved in the baseline and layout JSON.\n\n";
            var identity = $"Original PDF: [open]({stem}.pdf)\n\nSource SHA-256: `{prepared.SourceSha256}`\n\n";
            var sections = new StringBuilder(notice).Append(identity);
            var entries = new StringBuilder(notice).Append(identity)
                .Append("Rows preserve horizontal associations but can interleave independent columns.\n\n");
            foreach (var page in pages)
            {
                sections.Append("## PDF page ").Append(page.Number).Append("\n\n").Append(page.Layout!.Sections).Append("\n\n");
                entries.Append("## PDF page ").Append(page.Number).Append("\n\n").Append(page.Layout!.Entries).Append("\n\n");
            }
            await CreateFileAsync(Path.Combine(directory, $"{stem}.sections.md"), Encoding.UTF8.GetBytes(sections.ToString()), cancellationToken);
            await CreateFileAsync(Path.Combine(directory, $"{stem}.entries.md"), Encoding.UTF8.GetBytes(entries.ToString()), cancellationToken);
            await CreateFileAsync(Path.Combine(directory, $"{stem}.layout.json"),
                JsonSerializer.SerializeToUtf8Bytes(pages.Select(page => new
                {
                    Page = page.Number,
                    LayoutAlgorithm = DocumentLayoutReview.Version,
                    prepared.SourceSha256,
                    CoordinateOrigin = "PDF bottom-left",
                    Layout = page.Layout,
                })), cancellationToken);
        }
    }

    /// <summary>Writes the completed bundle's navigation page after all requested sources were generated.</summary>
    public static Task WriteIndexAsync(string directory, IReadOnlyList<string> stems, CancellationToken cancellationToken)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; base-uri 'none'; form-action 'none'">
            <title>Private document quality review</title></head><body>
            <h1>Private document quality review</h1>
            <p>Private original PDFs and extracted content. Do not commit, publish or upload this bundle.
            Open each review in a local desktop browser; if embedded PDF viewing is unavailable, use its original-PDF link.</p>
            <p>Check a representative procedure/warning/table page and a vocabulary/column page before choosing extraction rules.
            Record missing, merged, reordered or incorrectly split content. These are baseline and experimental layout previews,
            not delivered semantic profiles. Reviews open the section candidate when available;
            original baseline views remain explicitly selectable for comparison.</p><ul>
            """);
        foreach (var stem in stems)
        {
            ValidateStem(stem);
            html.Append("<li><a href=\"").Append(stem).Append(".html\">").Append(stem).Append("</a></li>");
        }
        html.Append("</ul></body></html>");
        return CreateFileAsync(Path.Combine(directory, "index.html"), Encoding.UTF8.GetBytes(html.ToString()), cancellationToken);
    }

    private static void ValidateStem(string stem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        if (!stem.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Review filenames require an ASCII alphanumeric/hyphen identifier.", nameof(stem));
    }

    private static void AppendView(StringBuilder html, string kind, string text) =>
        html.Append("<section data-kind=\"").Append(kind).Append("\"><pre>")
            .Append(WebUtility.HtmlEncode(text)).Append("</pre></section>");

    /// <summary>Adds display-only separation around numbered uppercase heading candidates, without reordering or deleting text.</summary>
    internal static string AddHeadingSpacing(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var spaced = new List<string>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var heading = Regex.IsMatch(lines[index].Trim(),
                @"^[0-9]+(?:\.[0-9]+)*[ \t]+\p{Lu}[^\p{Ll}\r\n]*$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (heading && spaced.Count > 0 && !string.IsNullOrWhiteSpace(spaced[^1]))
                spaced.Add(string.Empty);
            spaced.Add(lines[index]);
            if (heading && index + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[index + 1]))
                spaced.Add(string.Empty);
        }
        return string.Join("\n", spaced);
    }

    private static void AppendFencedText(StringBuilder markdown, string text)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        markdown.Append(fence).Append("text\n").Append(text).Append('\n').Append(fence).Append("\n\n");
    }

    private static async Task CreateFileAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 65_536, useAsync: true);
        await stream.WriteAsync(contents, cancellationToken);
    }
}
#endif
