#if DEBUG
using CasCap.Constants;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks reviewed heading and layout projections against exact immutable synthetic source spans.</summary>
[Trait("Category", "MarkdownChunking")]
public sealed class DocumentMarkdownStructureTests
{
    private readonly DocumentChunkingService _chunker = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ReviewedHeadingAncestry()
    {
        DocumentMarkdownPage[] pages = [new(1, "# 1 Root\n\n## 1.1 Symbols\n\nKeep.\n\n# Important\n\nNotice.\n\n# 1.2 Safety\n\n## General\n\nWarning.\n\n# 2 Next\n\nBody.")];
        var review = Review(pages) with
        {
            Headings = [Heading(pages[0], "# Important", 3), Heading(pages[0], "# 1.2 Safety", 2), Heading(pages[0], "## General", 3)],
        };
        var baseline = Prepare(pages);
        var result = Prepare(pages, review);
        Assert.Equal("# 1 Root\n## 1.1 Symbols\n### Important\n\nNotice.", result.Chunks.Single(chunk => chunk.Body == "Notice.").EmbeddingText);
        Assert.Equal("# 1 Root\n## 1.2 Safety\n### General\n\nWarning.", result.Chunks.Single(chunk => chunk.Body == "Warning.").EmbeddingText);
        Assert.Equal("# 2 Next\n\nBody.", result.Chunks.Last().EmbeddingText);
        Assert.Equal(baseline.MarkdownSha256, result.MarkdownSha256);
        Assert.Equal(baseline.Chunks.Single(chunk => chunk.Body == "Notice.").HeadingPath.Last().SourceSpans,
            result.Chunks.Single(chunk => chunk.Body == "Notice.").HeadingPath.Last().SourceSpans);
        Assert.Equal(review.Id, result.Review!.Id);
        Assert.NotSame(review.Headings, result.Review.Headings);
        Assert.NotEqual(baseline.Id, result.Id);
        Assert.Equal(result.Id, Prepare(pages, review with { Headings = review.Headings.Reverse().ToArray() }).Id);
        Assert.NotEqual(result.Id, Prepare(pages, review with { Headings = [review.Headings[0] with { Level = 4 }, .. review.Headings.Skip(1)] }).Id);
        Assert.DoesNotContain(review.Headings[0].Reason, string.Join('\n', result.Chunks.Select(chunk => chunk.EmbeddingText)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Control overview")]
    public void ExplicitParagraphPromotionRespectsPageGaps(string label)
    {
        DocumentMarkdownPage[] pages = [new(1, "# Previous\n\nEarlier."), new(3, label + "\n\n- Entry\n  Example.")];
        var review = Review(pages) with { Headings = [Heading(pages[1], label, 1)] };
        var result = Prepare(pages, review);
        var chunk = result.Chunks.Last();
        Assert.Equal("# " + label + "\n\n- Entry\n  Example.", chunk.EmbeddingText);
        Assert.Equal(new DocumentMarkdownSourceSpan(3, 0, label.Length, 2), Assert.Single(Assert.Single(chunk.HeadingPath).SourceSpans));
        Assert.Equal(label + "\n\n- Entry\n  Example.", pages[1].Markdown);
    }

    [Theory]
    [InlineData("> Quoted")]
    [InlineData("- Entry")]
    [InlineData("```\nLiteral\n```")]
    [InlineData("|  |  |\n|---|---|\n| Left | Right |")]
    [InlineData("First line\nSecond line")]
    public void UnsupportedHeadingBlockIsRejected(string markdown)
    {
        DocumentMarkdownPage[] pages = [new(1, markdown)];
        Assert.Throws<InvalidDataException>(() => Prepare(pages,
            Review(pages) with { Headings = [Heading(pages[0], markdown, 1)] }));
    }

    [Theory]
    [InlineData("missing-headings")]
    [InlineData("missing-tables")]
    [InlineData("null-heading")]
    [InlineData("null-table")]
    [InlineData("zero-level")]
    [InlineData("deep-level")]
    [InlineData("heading-reason")]
    [InlineData("table-reason")]
    [InlineData("reading-order")]
    [InlineData("partial-heading")]
    [InlineData("partial-table")]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    public void InvalidStructuralReviewIsRejected(string failure)
    {
        DocumentMarkdownPage[] pages = [new(1, "C\n\nBody.")];
        var heading = Heading(pages[0], "C", 1);
        var table = new DocumentMarkdownLayoutTableReview(1, 0, 1, DocumentMarkdownReadingOrders.ColumnMajor, "Reviewed layout.");
        var review = Review(pages) with { Headings = [heading] };
        review = failure switch
        {
            "missing-headings" => review with { Headings = null! },
            "missing-tables" => review with { LayoutTables = null! },
            "null-heading" => review with { Headings = [null!] },
            "null-table" => review with { LayoutTables = [null!] },
            "zero-level" => review with { Headings = [heading with { Level = 0 }] },
            "deep-level" => review with { Headings = [heading with { Level = 7 }] },
            "heading-reason" => review with { Headings = [heading with { Reason = "" }] },
            "table-reason" => review with { Headings = [], LayoutTables = [table with { Reason = "" }] },
            "reading-order" => review with { Headings = [], LayoutTables = [table with { ReadingOrder = "guessed" }] },
            "partial-heading" => review with { Headings = [heading with { EndOffset = 2 }] },
            "partial-table" => review with { Headings = [], LayoutTables = [table with { EndOffset = 2 }] },
            "overlap" => review with { LayoutTables = [table] },
            "duplicate" => review with { Headings = [heading, heading] },
            "limit" => review with { Headings = Enumerable.Repeat(heading, 5_001).ToArray() },
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        Assert.Throws<InvalidDataException>(() => Prepare(pages, review));
    }

    [Theory]
    [InlineData("Alpha", "Beta")]
    [InlineData("a\\|b", "**Beta**")]
    [InlineData("\uD83D\uDE80", "Gr\u00FCn")]
    public void LayoutEntriesUseColumnOrderAndExactCellSpans(string left, string right)
    {
        var table = $"|  |  |\n|---|---|\n| {left} | {right} |\n| Gamma | Delta |\n| Last |  |";
        DocumentMarkdownPage[] pages = [new(1, "# Group\n\n" + table)];
        var review = Review(pages) with { LayoutTables = [Layout(pages[0], table)] };
        var baseline = Prepare(pages);
        var result = Prepare(pages, review);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal($"- {left}\n- Gamma\n- Last\n- {right}\n- Delta", chunk.Body);
        Assert.Equal([left, "Gamma", "Last", right, "Delta"],
            chunk.SourceSpans.Select(span => pages[0].Markdown[span.StartOffset..span.EndOffset]));
        Assert.All(chunk.SourceSpans, span => Assert.Equal(1, span.BlockOrdinal));
        Assert.Equal(table, Assert.Single(baseline.Chunks).Body);
        Assert.Equal(baseline.MarkdownSha256, result.MarkdownSha256);
        Assert.NotEqual(baseline.Id, result.Id);
        Assert.False(chunk.IsFragmented);
        Assert.NotSame(review.LayoutTables, result.Review!.LayoutTables);
        var roundTrip = JsonSerializer.Deserialize<DocumentMarkdownChunkingResult>(JsonSerializer.Serialize(result));
        Assert.Equal(result.Id, roundTrip!.Id);
        Assert.Contains("independent layout entries", DocumentPreparationReviewRenderer.RenderChunks(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Not a table.")]
    [InlineData("| Key | Value |\n|---|---|\n| Left | Right |")]
    public void UnsupportedLayoutIsRejected(string table)
    {
        DocumentMarkdownPage[] pages = [new(1, table)];
        Assert.Throws<InvalidDataException>(() => Prepare(pages,
            Review(pages) with { LayoutTables = [Layout(pages[0], table)] }));
    }

    [Theory]
    [InlineData("|  |  |\n|---|---|\n| Left |")]
    [InlineData("|  |  |\n|---|---|\n| Left |  |")]
    public void EmptyLayoutCellsDoNotInventSourceText(string table)
    {
        DocumentMarkdownPage[] pages = [new(1, "# Group\n\n" + table)];
        var result = Prepare(pages, Review(pages) with { LayoutTables = [Layout(pages[0], table)] });
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("- Left", chunk.Body);
        Assert.Equal("Left", pages[0].Markdown[Assert.Single(chunk.SourceSpans).StartOffset..chunk.SourceSpans[0].EndOffset]);
    }

    [Theory]
    [InlineData("Lost")]
    [InlineData("-")]
    public void LayoutRetainsAdditionalSourceColumns(string entry)
    {
        var table = $"|  |  |\n|---|---|\n| Left | Right | {entry} |";
        DocumentMarkdownPage[] pages = [new(1, table)];
        var result = Prepare(pages, Review(pages) with { LayoutTables = [Layout(pages[0], table)] });
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal($"- Left\n- Right\n- {entry}", chunk.Body);
        Assert.Equal(["Left", "Right", entry], chunk.SourceSpans.Select(span => table[span.StartOffset..span.EndOffset]));
    }

    [Fact]
    public void ReviewedLayoutIsAtomicWithinHeadingBudget()
    {
        var table = "|  |  |\n|---|---|\n" + string.Join('\n', Enumerable.Range(0, 80).Select(index => $"| Entry {index} | Example {index} |"));
        DocumentMarkdownPage[] pages = [new(1, "# Group\n\n" + table)];
        var review = Review(pages) with { LayoutTables = [Layout(pages[0], table)] };
        Assert.Throws<InvalidDataException>(() => _chunker.ChunkMarkdown(pages,
            new() { MaximumTokensPerChunk = 64, OverlapTokens = 0 }, new(), review, Token));
    }

    [Fact]
    public void SetextLevelAndRelationalTable()
    {
        const string heading = "Original\n========";
        const string table = "| Key | Value |\n|---|---|\n| Left | Right |";
        DocumentMarkdownPage[] pages = [new(1, "# Root\n\n" + heading + "\n\n" + table)];
        var result = Prepare(pages, Review(pages) with { Headings = [Heading(pages[0], heading, 2)] });
        Assert.Equal("# Root\n## Original\n\n" + table, Assert.Single(result.Chunks).EmbeddingText);
    }

    private DocumentMarkdownChunkingResult Prepare(DocumentMarkdownPage[] pages, DocumentMarkdownSourceReview? review = null) =>
        _chunker.ChunkMarkdown(pages, new(), new(), review, Token);

    private static DocumentMarkdownSourceReview Review(DocumentMarkdownPage[] pages) =>
        new(2, "synthetic-structure-review", DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages)), [], [], []);

    private static DocumentMarkdownHeadingReview Heading(DocumentMarkdownPage page, string text, int level) =>
        new(page.PageNumber, page.Markdown.IndexOf(text, StringComparison.Ordinal),
            page.Markdown.IndexOf(text, StringComparison.Ordinal) + text.Length, level, "Verified synthetic heading.");

    private static DocumentMarkdownLayoutTableReview Layout(DocumentMarkdownPage page, string text) =>
        new(page.PageNumber, page.Markdown.IndexOf(text, StringComparison.Ordinal),
            page.Markdown.IndexOf(text, StringComparison.Ordinal) + text.Length, DocumentMarkdownReadingOrders.ColumnMajor, "Verified synthetic layout.");
}
#endif
