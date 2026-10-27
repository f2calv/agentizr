#if DEBUG
using System.Text;

namespace CasCap.Tests;

/// <summary>Exports private managed/local page comparisons as escaped text, never executing source Markdown or HTML.</summary>
internal static class DocumentIntelligenceBenchmarkReview
{
    /// <summary>Writes immutable originals and page-selected comparisons, or verifies identical files when resuming.</summary>
    public static async Task WriteAsync(
        string directory,
        string stem,
        byte[] original,
        IReadOnlyDictionary<int, string> localPages,
        DocumentIntelligenceQualityClient.Result managed,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(stem) || !stem.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("A generic artifact name is required.", nameof(stem));
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; frame-src 'self' file:; base-uri 'none'; form-action 'none'">
            <title>Private managed extraction comparison</title>
            <style>body{font-family:system-ui;margin:1rem} .page{display:grid;grid-template-columns:1fr 1fr 1fr;gap:1rem}
            iframe{width:100%;height:85vh;border:0} pre{white-space:pre-wrap;overflow-wrap:anywhere;height:80vh;overflow:auto;border:1px solid #ccc;padding:1rem}
            @media(max-width:1000px){.page{grid-template-columns:1fr}iframe{height:65vh}}</style></head><body>
            <h1>Private managed extraction comparison</h1>
            <p>Experimental output; semantic acceptance is pending. All extracted text is escaped, not rendered.
            Page spans can omit document-level markers; retain the complete managed Markdown and raw analysis JSON.
            No chunking, embeddings, retrieval or level-policy approval was performed.</p><nav>
            """);
        foreach (var number in managed.Pages.Keys.Order())
            html.Append("<a href=\"#page-").Append(number).Append("\">PDF page ").Append(number).Append("</a> | ");
        html.Append("</nav><p><a href=\"index.html\">Review index</a> | <a href=\"").Append(stem)
            .Append(".pdf\">Original PDF</a> | <a href=\"").Append(stem)
            .Append(".managed.md\">Complete managed Markdown</a> | <a href=\"").Append(stem)
            .Append(".analysis.json\">Raw managed JSON</a></p>");
        var local = new StringBuilder("# Local comparison: " + DocumentLayoutReview.Version + "\n\n");
        foreach (var (number, text) in managed.Pages.OrderBy(pair => pair.Key))
        {
            var baseline = localPages[number];
            local.Append("## PDF page ").Append(number).Append("\n\n").Append(baseline).Append("\n\n");
            html.Append("<section id=\"page-").Append(number).Append("\"><h2>PDF page ").Append(number)
                .Append("</h2><div class=\"page\"><div><h3>Original</h3><iframe title=\"Original PDF page ")
                .Append(number).Append("\" src=\"").Append(stem).Append(".pdf#page=").Append(number)
                .Append("&amp;view=FitH\"></iframe></div><div><h3>Local ").Append(DocumentLayoutReview.Version)
                .Append("</h3><pre>").Append(WebUtility.HtmlEncode(baseline))
                .Append("</pre></div><div><h3>Document Intelligence</h3><pre>")
                .Append(WebUtility.HtmlEncode(text)).Append("</pre></div></div></section>");
        }
        html.Append("</body></html>");
        await SaveAsync(Path.Combine(directory, stem + ".pdf"), original, cancellationToken);
        await SaveAsync(Path.Combine(directory, stem + ".managed.md"), Encoding.UTF8.GetBytes(managed.Markdown), cancellationToken);
        await SaveAsync(Path.Combine(directory, stem + ".local.md"), Encoding.UTF8.GetBytes(local.ToString()), cancellationToken);
        await SaveAsync(Path.Combine(directory, stem + ".html"), Encoding.UTF8.GetBytes(html.ToString()), cancellationToken);
    }

    /// <summary>Publishes navigation only after both approved samples have completed.</summary>
    public static Task WriteIndexAsync(string directory, CancellationToken cancellationToken) =>
        SaveAsync(Path.Combine(directory, "index.html"), Encoding.UTF8.GetBytes("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; base-uri 'none'; form-action 'none'">
            <title>Private Document Intelligence benchmark</title></head><body>
            <h1>Private Document Intelligence benchmark</h1>
            <p>Two hash-pinned original PDFs; six analyzed pages. Contains private source and extracted content.
            Do not publish, commit or upload this directory. Open comparisons in a local browser.
            Technical contract checks are not semantic acceptance or A1-policy approval.</p>
            <ul><li><a href="source-1-sample-1.html">Sample 1: PDF pages 2, 3, 6</a></li>
            <li><a href="source-2-sample-2.html">Sample 2: PDF pages 6, 7, 12</a></li></ul></body></html>
            """), cancellationToken);

    /// <summary>Creates a private artifact or verifies exact bytes when resuming; never replaces different content.</summary>
    internal static async Task SaveAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!existing.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Existing private review artifact differs; refusing to overwrite.");
            return;
        }
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken);
    }
}
#endif
