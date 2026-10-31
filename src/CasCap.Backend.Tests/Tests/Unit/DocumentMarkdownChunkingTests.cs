#if DEBUG
using CasCap.Constants;
using Microsoft.ML.Tokenizers;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks source heading paths, bounded contextual inputs and honest page/Markdown provenance without providers.</summary>
[Trait("Category", "MarkdownChunking")]
public sealed class DocumentMarkdownChunkingTests
{
    private readonly DocumentChunkingService _chunker = new();
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    [Theory]
    [InlineData("# 7 CONTROL UNIT\n\n## 7.1 ADDITIONAL HEAT\n\nSource passage.")]
    [InlineData("7 CONTROL UNIT\n==============\n\n7.1 ADDITIONAL HEAT\n-------------------\n\nSource passage.")]
    public void HeadingHierarchy(string markdown)
    {
        var result = Prepare([new(6, markdown)]);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("# 7 CONTROL UNIT\n## 7.1 ADDITIONAL HEAT\n\nSource passage.", chunk.EmbeddingText);
        Assert.Equal("Source passage.", chunk.Body);
        Assert.Equal([1, 2], chunk.HeadingPath.Select(heading => heading.Level));
        Assert.Equal(6, Assert.Single(chunk.SourceSpans).PageNumber);
        Assert.Equal("Source passage.", Slice(markdown, Assert.Single(chunk.SourceSpans)));
        Assert.All(chunk.HeadingPath, heading => Assert.NotEmpty(heading.SourceSpans));
        Assert.DoesNotContain("CONTROL", chunk.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void SiblingsAndMissingAncestorLevels()
    {
        var result = Prepare([new(1, "# Root\n\n### First\n\nAlpha.\n\n### Second\n\nBeta.\n\n# Other\n\nGamma.")]);
        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal(["# Root", "### First"], result.Chunks[0].HeadingPath.Select(item => item.Markdown));
        Assert.Equal(["# Root", "### Second"], result.Chunks[1].HeadingPath.Select(item => item.Markdown));
        Assert.Equal(["# Other"], result.Chunks[2].HeadingPath.Select(item => item.Markdown));
        Assert.Equal(["Alpha.", "Beta.", "Gamma."], result.Chunks.Select(chunk => chunk.Body));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void PhysicalPageContinuity(int nextPage, bool contiguous)
    {
        DocumentMarkdownPage[] pages = [new(1, "# Root\n\nFirst passage."), new(nextPage, "Second passage.")];
        var result = Prepare(pages);
        if (contiguous)
        {
            var chunk = Assert.Single(result.Chunks);
            Assert.Equal([1, 2], chunk.SourceSpans.Select(span => span.PageNumber));
            Assert.Equal("# Root", Assert.Single(chunk.HeadingPath).Markdown);
            Assert.Equal(1, Assert.Single(Assert.Single(chunk.HeadingPath).SourceSpans).PageNumber);
        }
        else
        {
            Assert.Equal(2, result.Chunks.Count);
            Assert.Empty(result.Chunks[1].HeadingPath);
            Assert.Equal("Second passage.", result.Chunks[1].EmbeddingText);
        }
        AssertSourceSpans(pages, result);
    }

    [Fact]
    public void FencedAndNestedHeadingsAreNotSections()
    {
        var result = Prepare([new(1, "# Root\n\n```markdown\n# Not a section\n```\n\n> ## Quoted heading\n\nFinal `# literal` text.")]);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("# Root", Assert.Single(chunk.HeadingPath).Markdown);
        Assert.Contains("# Not a section", chunk.Body, StringComparison.Ordinal);
        Assert.Contains("> ## Quoted heading", chunk.Body, StringComparison.Ordinal);
        Assert.Contains("`# literal`", chunk.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(32, 4)]
    [InlineData(64, 16)]
    public void ContinuationsIncludeHeadingsAndStayScalarSafe(int maximum, int overlap)
    {
        var body = string.Concat(Enumerable.Repeat("Wartung \uD83D\uDE80 Gr\u00fc\u00dfe e\u0301 \u4F60\u597D. ", 35));
        DocumentMarkdownPage[] pages = [new(7, "# Root\n\n## Child\n\n" + body)];
        var result = Prepare(pages, new() { MaximumTokensPerChunk = maximum, OverlapTokens = overlap });
        Assert.True(result.Chunks.Count > 1);
        var reconstructed = new StringBuilder();
        var covered = pages[0].Markdown.IndexOf(body, StringComparison.Ordinal);
        var unit = result.Chunks[0].SourceSpans[0].BlockOrdinal;
        foreach (var chunk in result.Chunks)
        {
            Assert.StartsWith("# Root\n## Child\n\n", chunk.EmbeddingText, StringComparison.Ordinal);
            Assert.Equal(_tokenizer.CountTokens(chunk.EmbeddingText, considerNormalization: false), chunk.TokenCount);
            Assert.InRange(chunk.TokenCount, 1, maximum);
            Assert.DoesNotContain("\uFFFD", chunk.Body, StringComparison.Ordinal);
            Assert.False(char.IsLowSurrogate(chunk.Body[0]));
            Assert.False(char.IsHighSurrogate(chunk.Body[^1]));
            Assert.True(chunk.IsFragmented);
            var span = Assert.Single(chunk.SourceSpans);
            Assert.Equal(unit, span.BlockOrdinal);
            Assert.True(span.EndOffset > covered);
            Assert.InRange(span.StartOffset, 0, covered);
            Assert.InRange(_tokenizer.CountTokens(pages[0].Markdown.AsSpan(span.StartOffset, covered - span.StartOffset),
                considerNormalization: false), 0, overlap);
            reconstructed.Append(pages[0].Markdown.AsSpan(covered, span.EndOffset - covered));
            covered = span.EndOffset;
        }
        Assert.Equal(body, reconstructed.ToString());
        AssertSourceSpans(pages, result);
    }

    [Fact]
    public void TargetDoesNotSplitFittingSemanticUnits()
    {
        var result = Prepare([new(1, "# Root\n\nA complete paragraph containing several related source statements.\n\nAnother paragraph.")],
            new() { MaximumTokensPerChunk = 64, TargetTokensPerChunk = 8, OverlapTokens = 0 });
        Assert.Equal(2, result.Chunks.Count);
        Assert.All(result.Chunks, chunk => Assert.False(chunk.IsFragmented));
        Assert.Contains("several related source statements.", result.Chunks[0].Body, StringComparison.Ordinal);
        Assert.All(result.Chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 64));
    }

    [Fact]
    public void SoftTargetCannotRejectAnOtherwiseFittingScalar()
    {
        var source = "# Root\n\n## Child\n\n" + string.Concat(Enumerable.Repeat("\uD83D\uDE80", 50));
        var result = Prepare([new(1, source)],
            new() { MaximumTokensPerChunk = 32, TargetTokensPerChunk = 8, OverlapTokens = 0 });
        Assert.True(result.Chunks.Count > 1);
        Assert.All(result.Chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 32));
        Assert.Equal(string.Concat(Enumerable.Repeat("\uD83D\uDE80", 50)), string.Concat(result.Chunks.Select(chunk => chunk.Body)));
    }

    [Theory]
    [InlineData("# **Root** ###\n\nBody.", "# **Root**")]
    [InlineData("# Root \\#\n\nBody.", "# Root \\#")]
    [InlineData("Root\r\n====\r\n\r\nBody.", "# Root")]
    [InlineData("Long\nwrapped title\n===\n\nBody.", "# Long wrapped title")]
    public void HeadingSourceMarkup(string markdown, string expected) =>
        Assert.Equal(expected, Assert.Single(Assert.Single(Prepare([new(1, markdown)]).Chunks).HeadingPath).Markdown);

    [Fact]
    public void FencedCodeCanCrossContiguousPages()
    {
        DocumentMarkdownPage[] pages = [new(1, "# Root\n\n```\n# Literal"), new(2, "## Still literal\n```\n\nBody.")];
        var result = Prepare(pages);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("# Root", Assert.Single(chunk.HeadingPath).Markdown);
        Assert.Contains("## Still literal", chunk.Body, StringComparison.Ordinal);
        AssertSourceSpans(pages, result);
    }

    [Fact]
    public void ListEntriesAndTablesRemainWhole()
    {
        var markdown = "# Vocabulary\n\n- Headword: form.\n  - Associated example.\n- Other: another example.\n\n| Term | Meaning |\n| --- | --- |\n| A | B |";
        var result = Prepare([new(1, markdown)], new() { MaximumTokensPerChunk = 128, TargetTokensPerChunk = 8, OverlapTokens = 0 });
        Assert.Equal(3, result.Chunks.Count);
        Assert.Contains("Headword: form.", result.Chunks[0].Body, StringComparison.Ordinal);
        Assert.Contains("Associated example.", result.Chunks[0].Body, StringComparison.Ordinal);
        Assert.Contains("Other:", result.Chunks[1].Body, StringComparison.Ordinal);
        Assert.Contains("| Term | Meaning |", result.Chunks[2].Body, StringComparison.Ordinal);
        Assert.Contains("| A | B |", result.Chunks[2].Body, StringComparison.Ordinal);
        Assert.All(result.Chunks, chunk => Assert.False(chunk.IsFragmented));
    }

    [Theory]
    [InlineData("- Entry ")]
    [InlineData("> Warning ")]
    [InlineData("```\n")]
    public void OversizedStructuredUnitRequiresReview(string opening)
    {
        var markdown = "# Root\n\n" + opening + string.Concat(Enumerable.Repeat("critical source content ", 50));
        if (opening.StartsWith("```", StringComparison.Ordinal))
            markdown += "\n```";
        Assert.Throws<InvalidDataException>(() => Prepare([new(1, markdown)],
            new() { MaximumTokensPerChunk = 16, OverlapTokens = 0 }));
    }

    [Fact]
    public void PrefixCannotConsumeTheEntireBudget()
    {
        var heading = "# " + string.Concat(Enumerable.Repeat("Heading ", 30));
        Assert.Throws<InvalidDataException>(() => Prepare([new(1, heading + "\n\nBody.")],
            new() { MaximumTokensPerChunk = 8, OverlapTokens = 0 }));
    }

    [Fact]
    public void ProfileIdentityAndExactMarkdownDigest()
    {
        var settings = new DocumentMarkdownChunkingConfig { MaximumTokensPerChunk = 64, OverlapTokens = 4 };
        var first = Prepare([new(1, "# H\n\nText.")], settings);
        Assert.Equal(DocumentPreparationFormats.MarkdownHeadings, first.Profile.Strategy);
        Assert.Equal(first.Profile.Id, _chunker.GetMarkdownProfile(settings).Id);
        Assert.NotEqual(first.Profile.Id, _chunker.GetMarkdownProfile(settings with { TargetTokensPerChunk = 32 }).Id);
        Assert.NotEqual(first.Profile.Id, _chunker.GetMarkdownProfile(settings with { OverlapTokens = 0 }).Id);
        Assert.NotEqual(first.Profile.Id, (first.Profile with { HeadingPrefixVersion = "synthetic-change" }).Id);
        Assert.NotEqual(first.MarkdownSha256, Prepare([new(2, "# H\n\nText.")], settings).MarkdownSha256);
        Assert.NotEqual(first.MarkdownSha256, Prepare([new(1, "# H\n\nOther.")], settings).MarkdownSha256);
        var roundTrip = JsonSerializer.Deserialize<DocumentMarkdownChunkingProfile>(JsonSerializer.Serialize(first.Profile));
        Assert.Equal(first.Profile, roundTrip);
        Assert.Equal(first.Profile.Id, roundTrip!.Id);
    }

    [Theory]
    [InlineData(7, null, 0)]
    [InlineData(16, 17, 0)]
    [InlineData(16, 0, 0)]
    [InlineData(16, 8, 16)]
    [InlineData(16, 8, -1)]
    public void InvalidSettings(int maximum, int? target, int overlap) =>
        Assert.Throws<ValidationException>(() => _chunker.GetMarkdownProfile(new()
        {
            MaximumTokensPerChunk = maximum,
            TargetTokensPerChunk = target,
            OverlapTokens = overlap,
        }));

    [Fact]
    public void ResourceAndSourceOrderLimits()
    {
        Assert.Throws<ArgumentException>(() => Prepare([new(2, "Two"), new(1, "One")]));
        Assert.Throws<ArgumentException>(() => Prepare([new(1, "One"), new(1, "Again")]));
        Assert.Throws<ArgumentException>(() => Prepare([new(0, "Invalid")]));
        Assert.Throws<InvalidDataException>(() => Prepare([new(1, "Long text")], limits: new() { MaximumExtractedCharacters = 3 }));
        Assert.Throws<InvalidDataException>(() => Prepare([new(1, "# One\n\nFirst.\n\n# Two\n\nSecond.")], limits: new() { MaximumChunks = 1 }));
    }

    [Fact]
    public void HeadinglessAndEmptyContent()
    {
        var chunk = Assert.Single(Prepare([new(1, "Plain source.")]).Chunks);
        Assert.Empty(chunk.HeadingPath);
        Assert.Equal("Plain source.", chunk.EmbeddingText);
        Assert.Empty(Prepare([new(1, "# Heading only")]).Chunks);
        Assert.Empty(Prepare([]).Chunks);
    }

    [Fact]
    public void CancellationBeforeParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => _chunker.ChunkMarkdown(
            [new(1, "# H\n\nBody.")], new(), new(), cancellation.Token));
    }

    private DocumentMarkdownChunkingResult Prepare(
        DocumentMarkdownPage[] pages,
        DocumentMarkdownChunkingConfig? settings = null,
        DocumentPreparationConfig? limits = null
    ) => _chunker.ChunkMarkdown(pages, settings ?? new(), limits ?? new(), TestContext.Current.CancellationToken);

    private static void AssertSourceSpans(DocumentMarkdownPage[] pages, DocumentMarkdownChunkingResult result)
    {
        foreach (var chunk in result.Chunks)
        {
            Assert.NotEmpty(chunk.SourceSpans);
            foreach (var span in chunk.SourceSpans)
            {
                var page = Assert.Single(pages, item => item.PageNumber == span.PageNumber);
                Assert.InRange(span.StartOffset, 0, page.Markdown.Length - 1);
                Assert.InRange(span.EndOffset, span.StartOffset + 1, page.Markdown.Length);
                Assert.Contains(Slice(page.Markdown, span), chunk.Body, StringComparison.Ordinal);
            }
        }
    }

    private static string Slice(string markdown, DocumentMarkdownSourceSpan span) => markdown[span.StartOffset..span.EndOffset];
}
#endif
