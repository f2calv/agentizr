#if DEBUG
using CasCap.Constants;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Microsoft.ML.Tokenizers;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Rechecks retained transcription exports locally without providers, paid ledgers or source extraction.</summary>
[Trait("Category", "OfflineDocumentReview")]
public sealed class DocumentPreparationOfflineReviewTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Gets the separate local-artifact gate; ordinary runs never read operator documents.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("CASCAP_RUN_DOCUMENT_OFFLINE_REVIEW") == "true";

    [Fact]
    public void ExportTranscriptionsRoundTrip()
    {
        const string markdown = "# Source\n\nLiteral <tag>, &lt;literal&gt; and **body**.";
        var html = "<h2>Source 1, original page 3</h2><pre>Draft only</pre>"
            + DocumentPreparationReviewRenderer.Render(new(3, markdown, ["Changed draft"], ["Review only"]))
            + "<h2>Source 2, original page 7</h2>"
            + DocumentPreparationReviewRenderer.Render(new(7, "Second page.", [], []));
        var pages = DocumentPreparationReviewRenderer.ReadPages(html);
        Assert.Equal(2, pages.Count);
        Assert.Equal(new DocumentMarkdownPage(3, markdown), Assert.Single(pages[1]));
        Assert.Equal(new DocumentMarkdownPage(7, "Second page."), Assert.Single(pages[2]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<h2>Source 1, original page 3</h2>")]
    [InlineData("<pre class='transcription'>Unpaired</pre>")]
    [InlineData("<h2>Source 1, original page 2</h2><h2>Source 1, original page 3</h2><pre class='transcription'>Body</pre>")]
    [InlineData("<h2>Source 1, original page 2</h2><pre class='transcription'>A</pre><h2>Source 1, original page 2</h2><pre class='transcription'>B</pre>")]
    [InlineData("<h2>Source 1, original page 2</h2><pre class='transcription'><b>Not encoded</b></pre>")]
    public void RejectsUnpairedOrMalformedExport(string html) =>
        Assert.Throws<InvalidDataException>(() => DocumentPreparationReviewRenderer.ReadPages(html));

    [Fact]
    public void HeadingOnlySectionsRemainVisibleOutsideChunks()
    {
        DocumentMarkdownPage[] pages = [new(1, "# Empty section\n\n# Actual section\n\nBody.")];
        var result = new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), Token);
        var heading = Assert.Single(ValidateProjection(pages, result, TiktokenTokenizer.CreateForEncoding("cl100k_base")));
        Assert.Equal("# Empty section", pages[0].Markdown[heading.StartOffset..heading.EndOffset]);
        Assert.DoesNotContain("Empty section", Assert.Single(result.Chunks).EmbeddingText, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBodyIsNotAcceptedAsHeadingOnly()
    {
        DocumentMarkdownPage[] pages = [new(1, "# First\n\nFirst body.\n\n# Second\n\nSecond body.")];
        var result = new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), Token);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateProjection(pages,
            result with { Chunks = result.Chunks.Take(1).ToArray() }, TiktokenTokenizer.CreateForEncoding("cl100k_base")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutCoverageCannotHideDroppedCells(bool dropCell)
    {
        const string table = "|  |  |\n|---|---|\n| Alpha | Beta |";
        DocumentMarkdownPage[] pages = [new(1, table)];
        var review = new DocumentMarkdownSourceReview(2, "synthetic-layout-review",
            DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages)), [], [],
            [new(1, 0, table.Length, DocumentMarkdownReadingOrders.ColumnMajor, "Verified synthetic layout.")]);
        var result = new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), review, Token);
        var tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        if (dropCell)
        {
            var chunk = Assert.Single(result.Chunks);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateProjection(pages, result with
            {
                Chunks = [chunk with { SourceSpans = chunk.SourceSpans.Take(1).ToArray() }],
            }, tokenizer));
        }
        else
            Assert.Empty(ValidateProjection(pages, result, tokenizer));
    }

    [Fact]
    public void UnusedPromotedHeadingIsReported()
    {
        DocumentMarkdownPage[] pages = [new(1, "Body.\n\nC")];
        var review = new DocumentMarkdownSourceReview(2, "synthetic-heading-review",
            DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages)), [], [new(1, 7, 8, 1, "Verified marker.")], []);
        var result = new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), review, Token);
        var span = Assert.Single(ValidateProjection(pages, result, TiktokenTokenizer.CreateForEncoding("cl100k_base")));
        Assert.Equal(new HeadingOnlySpan(1, 7, 8), span);
    }

    [Fact]
    public void LayoutSyntaxAllowanceCannotHideExtraCells()
    {
        const string original = "|  |  |\n|---|---|\n| Alpha | Beta |";
        DocumentMarkdownPage[] pages = [new(1, original)];
        var review = new DocumentMarkdownSourceReview(2, "synthetic-layout-review",
            DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages)), [], [],
            [new(1, 0, original.Length, DocumentMarkdownReadingOrders.ColumnMajor, "Verified synthetic layout.")]);
        var result = new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), review, Token);
        var changed = new[] { new DocumentMarkdownPage(1, original + " Lost |") };
        result = result with { Review = review with { LayoutTables = [review.LayoutTables[0] with { EndOffset = changed[0].Markdown.Length }] } };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateProjection(changed, result, TiktokenTokenizer.CreateForEncoding("cl100k_base")));
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires explicit approval and retained local review input plus a fresh private output directory.")]
    public async Task RetainedCandidatesToChunks()
    {
        var input = Required("CASCAP_DOCUMENT_REVIEW_INPUT");
        var directory = Required("CASCAP_DOCUMENT_REVIEW_OUTPUT");
        Assert.True(Path.IsPathFullyQualified(input) && Path.IsPathFullyQualified(directory));
        Assert.False(Directory.Exists(directory), "Use a fresh private directory; retained evidence must not be overwritten.");
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        var artifactPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceReviews = new Dictionary<int, DocumentMarkdownSourceReview>();
        var reviewDirectory = Environment.GetEnvironmentVariable("CASCAP_DOCUMENT_REVIEW_ANNOTATIONS");
        if (reviewDirectory is not null)
            Assert.True(Path.IsPathFullyQualified(reviewDirectory), "Review annotations require an explicit absolute private directory.");
        var limits = new DocumentPreparationConfig();
        var html = Encoding.UTF8.GetString(await Read("index.html", 8 * 1024 * 1024));
        var pages = DocumentPreparationReviewRenderer.ReadPages(html);
        Assert.Equal([1, 2], pages.Keys.Order());
        foreach (var ordinal in pages.Keys.Order())
        {
            Assert.Equal(3, pages[ordinal].Length);
            var pdf = await Read($"source-{ordinal}.pdf", limits.MaximumPdfBytes);
            using var usage = JsonDocument.Parse(await Read($"source-{ordinal}.usage.json", 1024 * 1024));
            Assert.Equal(DocumentPreparationIdentity.Hash(pdf), usage.RootElement.GetProperty("originalSha256").GetString());
            var originalPageCount = usage.RootElement.GetProperty("originalPageCount").GetInt32();
            Assert.InRange(originalPageCount, 1, limits.MaximumPages);
            var selected = usage.RootElement.GetProperty("physicalPages").EnumerateArray().Select(page => page.GetInt32()).Order().ToArray();
            Assert.Equal(selected, pages[ordinal].Select(page => page.PageNumber));
            Assert.All(selected, page => Assert.InRange(page, 1, originalPageCount));
            var operations = usage.RootElement.GetProperty("operations").EnumerateArray().ToArray();
            Assert.Equal(4, operations.Length);
            Assert.All(operations, operation =>
                Assert.Equal(DocumentPreparationOperations.Validated, operation.GetProperty("status").GetString()));
            Assert.Equal(selected, operations.Where(operation =>
                operation.GetProperty("stage").GetString() == DocumentPreparationOperations.PreparationAgent)
                .Select(operation => operation.GetProperty("pageNumber").GetInt32()).Order());
            if (reviewDirectory is not null)
                sourceReviews.Add(ordinal, Assert.IsType<DocumentMarkdownSourceReview>(
                    JsonSerializer.Deserialize<DocumentMarkdownSourceReview>(
                        await Read($"source-{ordinal}.review.json", 1024 * 1024, reviewDirectory), EvidenceJson)));
        }
        Directory.CreateDirectory(directory);
        var review = new StringBuilder(html.Replace("Production preparation review", "Offline heading chunk review", StringComparison.Ordinal))
            .Append("<hr><h1>Offline markdown-headings review</h1><p>Saved normalized transcriptions only; no provider calls, extraction, generation persistence or indexing. "
                + "Source spans are Markdown offsets, not PDF regions. Usage exports are unchanged. Mechanical checks are not semantic acceptance.</p>");
        var reports = new List<SourceReport>();
        var chunker = new DocumentChunkingService();
        var tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        foreach (var ordinal in pages.Keys.Order())
        {
            var prefix = $"source-{ordinal}";
            File.Copy(Path.Combine(input, prefix + ".pdf"), Path.Combine(directory, prefix + ".pdf"), overwrite: false);
            await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".pages.json"),
                JsonSerializer.Serialize(pages[ordinal], EvidenceJson), Token);
            review.Append($"<h2>Source {ordinal}: review-only chunks</h2>");
            DocumentMarkdownChunkingResult result;
            try
            {
                result = chunker.ChunkMarkdown(pages[ordinal], new(), limits, sourceReviews.GetValueOrDefault(ordinal), Token);
            }
            catch (InvalidDataException error)
            {
                reports.Add(new(ordinal, pages[ordinal].Select(page => page.PageNumber).ToArray(), 0, 0, 0, 0, [], error.Message));
                review.Append($"<p>Rejected: {WebUtility.HtmlEncode(error.Message)}</p>");
                continue;
            }
            var headingOnly = ValidateProjection(pages[ordinal], result, tokenizer);
            var json = JsonSerializer.Serialize(result, EvidenceJson);
            Assert.Equal(DocumentPreparationIdentity.Hash(json), DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(
                chunker.ChunkMarkdown(pages[ordinal], new(), limits, sourceReviews.GetValueOrDefault(ordinal), Token), EvidenceJson)));
            await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".chunks.json"), json, Token);
            review.Append(DocumentPreparationReviewRenderer.RenderChunks(result));
            review.Append($"<p>Heading-only source spans not embedded: {headingOnly.Length}. "
                + $"Retained in {prefix}.pages.json; no heading ancestry was invented.</p><ul>");
            foreach (var span in headingOnly)
            {
                var page = pages[ordinal].Single(page => page.PageNumber == span.PageNumber);
                review.Append($"<li>Physical page {span.PageNumber}, Markdown offsets [{span.StartOffset}, {span.EndOffset}): "
                    + $"<code>{WebUtility.HtmlEncode(page.Markdown[span.StartOffset..span.EndOffset])}</code></li>");
            }
            review.Append("</ul>");
            reports.Add(new(ordinal, pages[ordinal].Select(page => page.PageNumber).ToArray(), result.Chunks.Count,
                result.Chunks.Min(chunk => chunk.TokenCount), result.Chunks.Max(chunk => chunk.TokenCount),
                result.Chunks.Count(chunk => chunk.IsFragmented), headingOnly, null)
            {
                ExcludedSourceSpans = result.Review?.Exclusions.Count ?? 0,
                ReviewedHeadings = result.Review?.Headings.Count ?? 0,
                ReviewedLayoutTables = result.Review?.LayoutTables.Count ?? 0,
            });
        }
        foreach (var artifact in originals)
            Assert.Equal(artifact.Value, DocumentPreparationIdentity.Hash(await File.ReadAllBytesAsync(artifactPaths[artifact.Key], Token)));
        await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), review.ToString(), Token);
        await File.WriteAllTextAsync(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(new
        {
            scope = "retained-normalized-transcriptions-only",
            originalArtifacts = originals,
            sources = reports,
            originalArtifactsUnchanged = true,
            newProviderCalls = 0,
            persistedGenerations = 0,
            embeddedChunks = 0,
        }, EvidenceJson), Token);
        foreach (var report in reports)
            output.WriteLine($"Source ordinal {report.SourceOrdinal}: {report.ChunkCount} chunks, {report.MinimumTokens}-{report.MaximumTokens} tokens, "
                + $"{report.FragmentedChunks} paragraph fragments, {report.HeadingOnlySpans.Length} unembedded heading spans, "
                + $"{report.ExcludedSourceSpans} exclusions, {report.ReviewedHeadings} heading corrections, "
                + $"{report.ReviewedLayoutTables} layout tables; rejected: {report.Rejection is not null}.");
        Assert.All(reports, report => Assert.Null(report.Rejection));

        async Task<byte[]> Read(string name, long maximumBytes, string? root = null)
        {
            var path = Path.Combine(root ?? input, name);
            Assert.InRange(new FileInfo(path).Length, 1, maximumBytes);
            var bytes = await File.ReadAllBytesAsync(path, Token);
            Assert.InRange(bytes.LongLength, 1, maximumBytes);
            originals.Add(name, DocumentPreparationIdentity.Hash(bytes));
            artifactPaths.Add(name, path);
            return bytes;
        }
    }

    private static HeadingOnlySpan[] ValidateProjection(DocumentMarkdownPage[] pages, DocumentMarkdownChunkingResult result, TiktokenTokenizer tokenizer)
    {
        Assert.NotEmpty(result.Chunks);
        Assert.Equal(DocumentPreparationFormats.MarkdownHeadings, result.Profile.Strategy);
        var coverage = pages.ToDictionary(page => page.PageNumber, page => new bool[page.Markdown.Length]);
        foreach (var chunk in result.Chunks)
        {
            Assert.InRange(chunk.TokenCount, 1, result.Profile.MaximumTokensPerChunk);
            Assert.Equal(tokenizer.CountTokens(chunk.EmbeddingText, considerNormalization: false), chunk.TokenCount);
            var headings = string.Join('\n', chunk.HeadingPath.Select(heading => heading.Markdown));
            Assert.True(chunk.EmbeddingText == (headings.Length == 0 ? "" : headings + "\n\n") + chunk.Body,
                $"Chunk {chunk.Ordinal} differs from its heading/body projection.");
            Assert.NotEmpty(chunk.SourceSpans);
            foreach (var span in chunk.SourceSpans)
            {
                var text = Span(span);
                Assert.True(chunk.Body.Contains(text, StringComparison.Ordinal), $"Chunk {chunk.Ordinal} has an unmapped body span.");
            }
            var previousLevel = 0;
            foreach (var heading in chunk.HeadingPath)
            {
                Assert.InRange(heading.Level, previousLevel + 1, 6);
                previousLevel = heading.Level;
                Assert.NotEmpty(heading.SourceSpans);
                foreach (var span in heading.SourceSpans)
                {
                    Span(span);
                    Assert.All(Enumerable.Range(span.PageNumber, chunk.SourceSpans[0].PageNumber - span.PageNumber + 1),
                        number => Assert.Contains(number, coverage.Keys));
                }
            }
        }
        foreach (var exclusion in result.Review?.Exclusions ?? [])
        {
            Assert.False(coverage[exclusion.PageNumber].Skip(exclusion.StartOffset)
                .Take(exclusion.EndOffset - exclusion.StartOffset).Any(value => value),
                "A reviewed exclusion still contributes body or heading text.");
            Array.Fill(coverage[exclusion.PageNumber], true, exclusion.StartOffset, exclusion.EndOffset - exclusion.StartOffset);
        }
        ValidateLayoutCoverage(pages, result, coverage);
        var headingOnly = HeadingOnlySpans(pages, coverage, result.Review);
        foreach (var page in pages)
            for (var index = 0; index < page.Markdown.Length; index++)
                Assert.True(coverage[page.PageNumber][index] || char.IsWhiteSpace(page.Markdown[index]),
                    $"Physical page {page.PageNumber} has an uncovered non-whitespace character at offset {index}.");
        return headingOnly;

        string Span(DocumentMarkdownSourceSpan span)
        {
            var page = Assert.Single(pages, page => page.PageNumber == span.PageNumber);
            Assert.InRange(span.StartOffset, 0, page.Markdown.Length - 1);
            Assert.InRange(span.EndOffset, span.StartOffset + 1, page.Markdown.Length);
            Assert.False(char.IsLowSurrogate(page.Markdown[span.StartOffset]));
            Assert.False(char.IsHighSurrogate(page.Markdown[span.EndOffset - 1]));
            Array.Fill(coverage[span.PageNumber], true, span.StartOffset, span.EndOffset - span.StartOffset);
            return page.Markdown[span.StartOffset..span.EndOffset];
        }
    }

    private static void ValidateLayoutCoverage(DocumentMarkdownPage[] pages, DocumentMarkdownChunkingResult result, Dictionary<int, bool[]> coverage)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        foreach (var annotation in result.Review?.LayoutTables ?? [])
        {
            var page = Assert.Single(pages, page => page.PageNumber == annotation.PageNumber);
            var table = Assert.Single(Markdown.Parse(page.Markdown, pipeline).OfType<Table>(),
                table => table.Span.Start == annotation.StartOffset && table.Span.End + 1 == annotation.EndOffset);
            var rows = table.Cast<TableRow>().ToArray();
            var expected = new List<string>();
            for (var column = 0; column < rows[0].Count; column++)
                foreach (var row in rows.Where(row => !row.IsHeader))
                {
                    var cell = (TableCell)row[column];
                    var raw = page.Markdown[cell.Span.Start..(cell.Span.End + 1)];
                    var text = raw.Trim();
                    if (text.Length == 0)
                        continue;
                    var start = cell.Span.Start + raw.IndexOf(text, StringComparison.Ordinal);
                    var end = start + text.Length;
                    Assert.True(coverage[page.PageNumber].Skip(start).Take(text.Length).All(value => value), "A layout entry was dropped.");
                    Assert.Single(result.Chunks.SelectMany(chunk => chunk.SourceSpans), span =>
                        span.PageNumber == page.PageNumber && span.StartOffset == start && span.EndOffset == end);
                    expected.Add("- " + text);
                }
            Assert.Contains(result.Chunks, chunk => chunk.Body.Contains(string.Join('\n', expected), StringComparison.Ordinal));
            var line = 0;
            for (var offset = annotation.StartOffset; offset < annotation.EndOffset; offset++)
            {
                var value = page.Markdown[offset];
                if (value == '\n')
                    line++;
                Assert.True(coverage[page.PageNumber][offset] || char.IsWhiteSpace(value) || value == '|'
                    || (line == 1 && value is '-' or ':'), "Unmapped layout content is not table formatting.");
                coverage[page.PageNumber][offset] = true;
            }
        }
    }

    private static HeadingOnlySpan[] HeadingOnlySpans(DocumentMarkdownPage[] pages, Dictionary<int, bool[]> coverage, DocumentMarkdownSourceReview? review)
    {
        var result = new List<HeadingOnlySpan>();
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        for (var first = 0; first < pages.Length;)
        {
            var last = first + 1;
            while (last < pages.Length && pages[last].PageNumber == pages[last - 1].PageNumber + 1)
                last++;
            var partition = pages[first..last];
            var text = string.Join("\n\n", partition.Select(page => page.Markdown));
            foreach (var heading in Markdown.Parse(text, pipeline).OfType<HeadingBlock>())
            {
                var offset = 0;
                foreach (var page in partition)
                {
                    var start = Math.Max(0, heading.Span.Start - offset);
                    var end = Math.Min(page.Markdown.Length, heading.Span.End + 1 - offset);
                    if (start < end && Enumerable.Range(start, end - start).Any(index => !char.IsWhiteSpace(page.Markdown[index]))
                        && Enumerable.Range(start, end - start).All(index => !coverage[page.PageNumber][index]))
                    {
                        result.Add(new(page.PageNumber, start, end));
                        Array.Fill(coverage[page.PageNumber], true, start, end - start);
                    }
                    offset += page.Markdown.Length + 2;
                }
            }
            first = last;
        }
        foreach (var heading in review?.Headings ?? [])
            if (coverage[heading.PageNumber].Skip(heading.StartOffset).Take(heading.EndOffset - heading.StartOffset).All(value => !value))
            {
                result.Add(new(heading.PageNumber, heading.StartOffset, heading.EndOffset));
                Array.Fill(coverage[heading.PageNumber], true, heading.StartOffset, heading.EndOffset - heading.StartOffset);
            }
        return result.ToArray();
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Explicit {name} is required.");

    /// <summary>Summarizes local mechanical checks without copying document content into runner output.</summary>
    private sealed record SourceReport(int SourceOrdinal, int[] PhysicalPages, int ChunkCount,
        int MinimumTokens, int MaximumTokens, int FragmentedChunks, HeadingOnlySpan[] HeadingOnlySpans, string? Rejection)
    {
        /// <summary>Gets the count of audited per-page spans deliberately ineligible for embedding input.</summary>
        public int ExcludedSourceSpans { get; init; }

        /// <summary>Gets the count of explicit verified heading levels and promoted markers.</summary>
        public int ReviewedHeadings { get; init; }

        /// <summary>Gets the count of headerless tables projected as column-major independent entries.</summary>
        public int ReviewedLayoutTables { get; init; }
    }

    /// <summary>Identifies retained heading-only Markdown that is deliberately absent from vector input.</summary>
    private sealed record HeadingOnlySpan(int PageNumber, int StartOffset, int EndOffset);
}
#endif
