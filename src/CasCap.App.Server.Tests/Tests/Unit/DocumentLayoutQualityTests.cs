#if DEBUG
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CasCap.Tests.Unit;

/// <summary>Uses independent visual-order anchors to expose layout and semantic-boundary limitations.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class DocumentLayoutQualityTests
{
    [Theory]
    [InlineData("MAINTENANCE", "WARNING: Disconnect power.", "1. Replace the filter.")]
    [InlineData("VOCABULARY", "die T\u00fcr: Bitte die T\u00fcr schliessen.", "das Ger\u00e4t: Das Ger\u00e4t ist aus.")]
    public async Task ReferencePassages(string heading, string first, string second)
    {
        using var stream = new MemoryStream(DocumentPreparationTestData.ReferenceLines(heading, first, second));
        var result = await new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()),
            new DocumentChunkingService()).PrepareAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(PdfPreparationStatus.Prepared, result.Status);
        var text = string.Join("\n", result.Chunks.Select(chunk => chunk.Text));
        Assert.True(DocumentLayoutQuality.InOrder(text, [heading, first, second]));
        Assert.All(new[] { first, second }, passage =>
            Assert.Contains(result.Chunks, chunk => DocumentLayoutQuality.Normalize(chunk.Text)
                .Contains(passage, StringComparison.Ordinal)));
    }

    [Fact]
    public void TwoColumnReadingOrder()
    {
        using var pdf = PdfDocument.Open(DocumentPreparationTestData.TwoColumns());
        var page = pdf.GetPage(1);
        string[] expected = ["LEFT first entry.", "LEFT second entry.", "RIGHT first entry.", "RIGHT second entry."];
        Assert.False(DocumentLayoutQuality.InOrder(ContentOrderTextExtractor.GetText(page), expected),
            "Revisit this characterization if the production extractor starts restoring column order.");
        Assert.True(DocumentLayoutQuality.InOrder(DocumentLayoutQuality.SpatialText(page), expected),
            "Spatial extraction must preserve the fixture's independently specified column order.");
    }

    [Fact]
    public void TableRowsAreNotIndependentColumns()
    {
        using var pdf = PdfDocument.Open(DocumentPreparationTestData.VocabularyTable());
        var page = pdf.GetPage(1);
        string[] expected = ["die Tuer", "Die Tuer ist offen.", "das Licht", "Das Licht ist aus."];
        Assert.True(DocumentLayoutQuality.InOrder(ContentOrderTextExtractor.GetText(page), expected));
        Assert.False(DocumentLayoutQuality.InOrder(DocumentLayoutQuality.SpatialText(page), expected),
            "Spatial column detection alone must not be mistaken for reconstructing table rows.");
    }

    [Fact]
    public void TokenBudgetDoesNotGuaranteeSemanticBoundaries()
    {
        const string text = "one two three four five six seven WARNING: disconnect power before servicing.";
        const string warning = "WARNING: disconnect power";
        var chunks = new DocumentChunkingService().ChunkPage(text, 1,
            new DocumentChunkingConfig { MaximumTokensPerChunk = 8, OverlapTokens = 0 },
            TestContext.Current.CancellationToken).ToArray();
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 8));
        Assert.Equal(text, string.Concat(chunks.Select(chunk => chunk.Text)));
        Assert.False(chunks.Any(chunk => chunk.Text.Contains(warning, StringComparison.Ordinal)),
            "Lossless token coverage alone must not be mistaken for preserving a warning together.");
    }

    [Theory]
    [InlineData("first second third", true)]
    [InlineData("first third second", false)]
    [InlineData("first second", false)]
    public void IndependentOrderExpectations(string text, bool expected) =>
        Assert.Equal(expected, DocumentLayoutQuality.InOrder(text, ["first", "second", "third"]));
}
#endif
