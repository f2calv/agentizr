#if DEBUG
using CasCap.Constants;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks exact-source role decisions, atomic exclusions and unchanged provenance without heuristic deletion.</summary>
[Trait("Category", "MarkdownChunking")]
public sealed class DocumentMarkdownReviewTests
{
    private readonly DocumentChunkingService _chunker = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DocumentMarkdownSourceRoles.RunningHeader)]
    [InlineData(DocumentMarkdownSourceRoles.RunningFooter)]
    [InlineData(DocumentMarkdownSourceRoles.Navigation)]
    public void OnlyExplicitReviewedBlocksAreExcluded(string role)
    {
        DocumentMarkdownPage[] pages = [new(1, "Repeated label\n\n# Root\n\nKeep this warning.\n\nRepeated label")];
        var baseline = Prepare(pages);
        var review = Review(pages, new DocumentMarkdownExclusion(1, 0, 14, role, "Review metadata, never embedding text."));
        var result = Prepare(pages, review);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("# Root\n\nKeep this warning.\n\nRepeated label", chunk.EmbeddingText);
        Assert.Equal(baseline.MarkdownSha256, result.MarkdownSha256);
        Assert.NotEqual(baseline.Id, result.Id);
        Assert.Equal(3, result.Profile.Version);
        Assert.NotEqual(result.Profile.Id, (result.Profile with { Version = 2 }).Id);
        Assert.Equal(baseline.Chunks.Last().SourceSpans.Last(), chunk.SourceSpans.Last());
        Assert.NotSame(review.Exclusions, result.Review!.Exclusions);
        Assert.Equal(review.Id, result.Review.Id);
        Assert.DoesNotContain(review.Exclusions[0].Reason, chunk.EmbeddingText, StringComparison.Ordinal);
        var roundTrip = JsonSerializer.Deserialize<DocumentMarkdownChunkingResult>(JsonSerializer.Serialize(result));
        Assert.Equal(result.Id, roundTrip!.Id);
        Assert.Contains("Reviewed source exclusions", DocumentPreparationReviewRenderer.RenderChunks(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("digest")]
    [InlineData("review-id")]
    [InlineData("long-id")]
    [InlineData("role")]
    [InlineData("reason")]
    [InlineData("long-reason")]
    [InlineData("page")]
    [InlineData("negative")]
    [InlineData("empty")]
    [InlineData("past-end")]
    [InlineData("whitespace")]
    [InlineData("limit")]
    [InlineData("missing")]
    [InlineData("null-entry")]
    public void InvalidReviewIsRejected(string failure)
    {
        DocumentMarkdownPage[] pages = [new(1, "Header\n\nBody.")];
        var exclusion = new DocumentMarkdownExclusion(1, 0, 6, DocumentMarkdownSourceRoles.RunningHeader, "Reviewed header.");
        var review = Review(pages, exclusion);
        review = failure switch
        {
            "version" => review with { Version = 1 },
            "digest" => review with { MarkdownSha256 = "stale" },
            "review-id" => review with { ReviewId = " " },
            "long-id" => review with { ReviewId = new string('x', 129) },
            "role" => review with { Exclusions = [exclusion with { Role = "body" }] },
            "reason" => review with { Exclusions = [exclusion with { Reason = "" }] },
            "long-reason" => review with { Exclusions = [exclusion with { Reason = new string('x', 1_025) }] },
            "page" => review with { Exclusions = [exclusion with { PageNumber = 2 }] },
            "negative" => review with { Exclusions = [exclusion with { StartOffset = -1 }] },
            "empty" => review with { Exclusions = [exclusion with { EndOffset = 0 }] },
            "past-end" => review with { Exclusions = [exclusion with { EndOffset = 50 }] },
            "whitespace" => review with { Exclusions = [exclusion with { StartOffset = 6, EndOffset = 8 }] },
            "limit" => review with { Exclusions = Enumerable.Repeat(exclusion, 5_001).ToArray() },
            "missing" => review with { Exclusions = null! },
            "null-entry" => review with { Exclusions = [null!] },
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        Assert.Throws<InvalidDataException>(() => Prepare(pages, review));
    }

    [Theory]
    [InlineData("Keep this warning and its action.")]
    [InlineData("> Keep this warning\n> and its action.")]
    [InlineData("- Keep this entry\n  and its example.")]
    [InlineData("| Term | Meaning |\n|---|---|\n| Keep | Example |")]
    [InlineData("```\nKeep this literal block\n```")]
    public void PartialAtomicBlockCannotBeRemoved(string body)
    {
        DocumentMarkdownPage[] pages = [new(1, "# Root\n\n" + body)];
        var start = pages[0].Markdown.IndexOf("Keep", StringComparison.Ordinal);
        var review = Review(pages, new DocumentMarkdownExclusion(1, start, start + 4, DocumentMarkdownSourceRoles.RunningHeader, "Invalid partial selection."));
        Assert.Throws<InvalidDataException>(() => Prepare(pages, review));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void SurrogateBoundaryCannotBeCut(int start, int end)
    {
        DocumentMarkdownPage[] pages = [new(1, "\uD83D\uDE80 header\n\nBody.")];
        Assert.Throws<InvalidDataException>(() => Prepare(pages,
            Review(pages, new DocumentMarkdownExclusion(1, start, end, DocumentMarkdownSourceRoles.RunningHeader, "Invalid scalar boundary."))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DuplicateOrOverlappingSpansAreRejected(int secondStart)
    {
        DocumentMarkdownPage[] pages = [new(1, "Header\n\nBody.")];
        Assert.Throws<InvalidDataException>(() => Prepare(pages, Review(pages,
            new(1, 0, 6, DocumentMarkdownSourceRoles.RunningHeader, "First."),
            new(1, secondStart, 6, DocumentMarkdownSourceRoles.RunningHeader, "Second."))));
    }

    [Fact]
    public void ReviewedPageHeadingDoesNotResetRealAncestry()
    {
        DocumentMarkdownPage[] pages = [new(1, "# Root\n\nFirst."), new(2, "# Running heading\n\nSecond.")];
        var result = Prepare(pages, Review(pages,
            new DocumentMarkdownExclusion(2, 0, 17, DocumentMarkdownSourceRoles.RunningHeader, "Reviewed running title.")));
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("# Root", Assert.Single(chunk.HeadingPath).Markdown);
        Assert.Equal("# Root\n\nFirst.\n\nSecond.", chunk.EmbeddingText);
        Assert.Equal([1, 2], chunk.SourceSpans.Select(span => span.PageNumber));
    }

    [Fact]
    public void NavigationStaysInAuditNotEmbeddingText()
    {
        DocumentMarkdownPage[] pages = [new(1, "# Contents\n\nNavigation entry.\n\n# Actual\n\nKeep this warning.")];
        var review = Review(pages,
            new(1, 0, 10, DocumentMarkdownSourceRoles.Navigation, "Retained navigation heading."),
            new(1, 12, 29, DocumentMarkdownSourceRoles.Navigation, "Retained navigation body."));
        var result = Prepare(pages, review);
        Assert.Equal("# Actual\n\nKeep this warning.", Assert.Single(result.Chunks).EmbeddingText);
        Assert.Equal(2, result.Review!.Exclusions.Count);
        Assert.Contains("Navigation entry.", pages[0].Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewIdentityIncludesDecisionsButNotAnnotationOrder()
    {
        DocumentMarkdownPage[] pages = [new(1, "Header\n\nBody.\n\nFooter")];
        var review = Review(pages,
            new(1, 0, 6, DocumentMarkdownSourceRoles.RunningHeader, "Header."),
            new(1, 15, 21, DocumentMarkdownSourceRoles.RunningFooter, "Footer."));
        var result = Prepare(pages, review);
        Assert.Equal(result.Id, Prepare(pages, review with { Exclusions = review.Exclusions.Reverse().ToArray() }).Id);
        Assert.NotEqual(result.Id, Prepare(pages, review with { ReviewId = "another-review" }).Id);
        Assert.NotEqual(result.Id, Prepare(pages, review with
        {
            Exclusions = [review.Exclusions[0] with { Reason = "Updated review decision." }, review.Exclusions[1]],
        }).Id);
    }

    [Fact]
    public void CrossPageBlockRequiresEveryMappedSpan()
    {
        DocumentMarkdownPage[] pages = [new(1, "```\nHeader"), new(2, "Footer\n```\n\nBody.")];
        var first = new DocumentMarkdownExclusion(1, 0, 10, DocumentMarkdownSourceRoles.RunningHeader, "Reviewed complete block.");
        var second = new DocumentMarkdownExclusion(2, 0, 10, DocumentMarkdownSourceRoles.RunningHeader, "Reviewed complete block.");
        Assert.Throws<InvalidDataException>(() => Prepare(pages, Review(pages, first)));
        var result = Prepare(pages, Review(pages, first, second));
        Assert.Equal("Body.", Assert.Single(result.Chunks).Body);
        Assert.Equal(2, result.Review!.Exclusions.Count);
    }

    [Fact]
    public void WhollyExcludedInputIsNotAnIndexedGeneration()
    {
        DocumentMarkdownPage[] pages = [new(1, "Only furniture")];
        var result = Prepare(pages, Review(pages,
            new DocumentMarkdownExclusion(1, 0, 14, DocumentMarkdownSourceRoles.RunningHeader, "Reviewed complete input.")));
        Assert.Empty(result.Chunks);
        Assert.Single(result.Review!.Exclusions);
        Assert.Equal("Only furniture", pages[0].Markdown);
    }

    private DocumentMarkdownChunkingResult Prepare(DocumentMarkdownPage[] pages, DocumentMarkdownSourceReview? review = null) =>
        _chunker.ChunkMarkdown(pages, new(), new(), review, Token);

    private static DocumentMarkdownSourceReview Review(DocumentMarkdownPage[] pages, params DocumentMarkdownExclusion[] exclusions) =>
        new(2, "synthetic-review", DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(pages)), exclusions, [], []);
}
#endif
