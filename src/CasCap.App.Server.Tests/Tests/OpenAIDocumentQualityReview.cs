#if DEBUG
using System.Text;
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Exports private four-candidate comparisons and mechanical word/number differences, not semantic acceptance.</summary>
internal static class OpenAIDocumentQualityReview
{
    /// <summary>Four independently labelled page candidates share the same original physical-page identity.</summary>
    internal sealed record Page(int Number, string Local, string Managed, OpenAIDocumentQualityClient.Result PdfOnly, OpenAIDocumentQualityClient.Result Hybrid);
    /// <summary>Case-sensitive token multiplicity changes ignore layout/order and can include legitimate extraction corrections.</summary>
    internal sealed record Differences(IReadOnlyDictionary<string, int> Added, IReadOnlyDictionary<string, int> Removed);

    /// <summary>Flags lexical/number discrepancies against the managed draft without asserting that the draft is correct.</summary>
    public static Differences Compare(string draft, string candidate)
    {
        var before = Tokens(draft);
        var after = Tokens(candidate);
        var added = after.Where(pair => pair.Value > before.GetValueOrDefault(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value - before.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
        var removed = before.Where(pair => pair.Value > after.GetValueOrDefault(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value - after.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
        return new(added, removed);
    }

    /// <summary>Writes exact per-page model Markdown, separate review notes/differences and escaped selectable views.</summary>
    public static async Task WriteAsync(string directory, byte[] original, IReadOnlyList<Page> pages, CancellationToken cancellationToken)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-src 'self' file:; base-uri 'none'; form-action 'none'">
            <title>Private PDF-only and hybrid comparison</title>
            <style>body{font-family:system-ui;margin:0}header{padding:1rem}main{display:grid;grid-template-columns:1fr 1fr;height:80vh}
            iframe{width:100%;height:100%;border:0}#text{overflow:auto;padding:1rem}pre{white-space:pre-wrap;overflow-wrap:anywhere}
            [hidden]{display:none!important}select{margin-right:1rem}@media(max-width:850px){main{grid-template-columns:1fr;height:auto}iframe{height:65vh}}</style>
            </head><body><header><h1>Private PDF-only and hybrid comparison</h1>
            <p>Original PDF is authoritative. Candidate text is escaped, not rendered. Neither cleaner Markdown,
            model-reported changes nor lexical differences prove fidelity or approve A1 vocabulary.
            Differences compare with the imperfect managed draft, ignore order/punctuation, and can flag legitimate corrections.</p>
            <p><a href="original.pdf">Original PDF</a> | <a href="prompt.txt">Frozen prompt</a> |
            <a href="summary.json">Token usage and review counts</a></p>
            <label>Physical PDF page <select id="page">
            """);
        foreach (var page in pages)
            html.Append("<option>").Append(page.Number).Append("</option>");
        html.Append("""
            </select></label><label>Candidate <select id="view">
            <option value="hybrid" selected>PDF + Document Intelligence draft: OpenAI hybrid</option>
            <option value="pdf-only">PDF only: OpenAI</option>
            <option value="managed">Document Intelligence</option>
            <option value="local">Local LayoutReviewV6 baseline</option>
            </select></label></header><main><iframe id="pdf" title="Original PDF"></iframe><div id="text">
            """);
        var summary = new List<object>();
        foreach (var page in pages)
        {
            foreach (var (kind, candidate) in new[] { ("pdf-only", page.PdfOnly), ("hybrid", page.Hybrid) })
            {
                var stem = $"page-{page.Number}-{kind}";
                var differences = Compare(page.Managed, candidate.Markdown);
                var audit = new
                {
                    PhysicalPage = page.Number,
                    Candidate = kind,
                    Prompt = OpenAIDocumentQualityClient.PromptVersion,
                    candidate.Model,
                    candidate.Changes,
                    candidate.Uncertainties,
                    candidate.InputTokens,
                    candidate.OutputTokens,
                    MechanicalDifferencesAgainstImperfectDraft = differences,
                    SemanticAcceptance = "Pending independent review; differences and model notes are not a fidelity proof.",
                };
                await SaveAsync(directory, stem + ".md", Encoding.UTF8.GetBytes(candidate.Markdown), cancellationToken);
                await SaveAsync(directory, stem + ".audit.json", JsonSerializer.SerializeToUtf8Bytes(audit), cancellationToken);
                summary.Add(new
                {
                    PhysicalPage = page.Number,
                    Candidate = kind,
                    candidate.InputTokens,
                    candidate.OutputTokens,
                    ReportedChanges = candidate.Changes.Length,
                    ReportedUncertainties = candidate.Uncertainties.Length,
                    AddedTokenOccurrences = differences.Added.Values.Sum(),
                    RemovedTokenOccurrences = differences.Removed.Values.Sum(),
                    HtmlTagCount = Regex.Matches(candidate.Markdown, "<[^>]+>").Count,
                });
            }
            html.Append("<section data-page=\"").Append(page.Number).Append("\"><h2>Physical PDF page ")
                .Append(page.Number).Append("</h2><p>");
            foreach (var kind in new[] { "pdf-only", "hybrid" })
                html.Append("<a href=\"page-").Append(page.Number).Append('-').Append(kind).Append(".md\">")
                    .Append(kind).Append(" Markdown</a> | <a href=\"page-").Append(page.Number).Append('-')
                    .Append(kind).Append(".audit.json\">").Append(kind).Append(" notes/differences</a> | ");
            html.Append("</p>");
            foreach (var (kind, text) in new[]
            {
                ("local", page.Local), ("managed", page.Managed),
                ("pdf-only", page.PdfOnly.Markdown), ("hybrid", page.Hybrid.Markdown),
            })
                html.Append("<div data-kind=\"").Append(kind).Append("\"><h3>").Append(kind)
                    .Append("</h3><pre>").Append(WebUtility.HtmlEncode(text)).Append("</pre></div>");
            html.Append("</section>");
        }
        html.Append("""
            </div></main><script>
            const page=document.getElementById('page'),view=document.getElementById('view');
            function show(){for(const panel of document.querySelectorAll('[data-page]'))panel.hidden=panel.dataset.page!==page.value;
            for(const panel of document.querySelectorAll('[data-kind]'))panel.hidden=panel.dataset.kind!==view.value;
            document.getElementById('pdf').src='original.pdf#page='+page.value+'&view=FitH';}
            page.addEventListener('change',show);view.addEventListener('change',show);show();
            </script></body></html>
            """);
        await SaveAsync(directory, "original.pdf", original, cancellationToken);
        await SaveAsync(directory, "prompt.txt", Encoding.UTF8.GetBytes(OpenAIDocumentQualityClient.Instructions), cancellationToken);
        await SaveAsync(directory, "summary.json", JsonSerializer.SerializeToUtf8Bytes(summary), cancellationToken);
        await SaveAsync(directory, "index.html", Encoding.UTF8.GetBytes(html.ToString()), cancellationToken);
    }

    private static Dictionary<string, int> Tokens(string text)
    {
        var plain = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " ")).Normalize(NormalizationForm.FormC);
        return Regex.Matches(plain, @"\p{L}[\p{L}\p{M}]*|\p{N}+(?:[.,]\p{N}+)*").Select(match => match.Value)
            .GroupBy(token => token, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static Task SaveAsync(string directory, string file, byte[] bytes, CancellationToken cancellationToken) =>
        DocumentIntelligenceBenchmarkReview.SaveAsync(Path.Combine(directory, file), bytes, cancellationToken);
}
#endif
