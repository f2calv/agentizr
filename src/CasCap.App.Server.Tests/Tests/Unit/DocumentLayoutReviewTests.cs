#if DEBUG
using UglyToad.PdfPig;

namespace CasCap.Tests.Unit;

/// <summary>Checks explicit candidate tradeoffs, source coverage, paragraph geometry, typography and conservative margin handling.</summary>
[Trait("Category", "DocumentQuality")]
public sealed class DocumentLayoutReviewTests
{
    [Fact]
    public void ParagraphsAndEmphasis()
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.LayoutPages(1)));
        Assert.Contains("### 1 OVERVIEW", result.Sections);
        Assert.Contains("First paragraph continues here.", result.Sections);
        Assert.Contains("here.\n\n", result.Sections);
        Assert.Contains("Second paragraph.", result.Sections);
        Assert.Equal(result.SourceWords, result.RetainedWords);
        Assert.NotEmpty(result.Coordinates);
        Assert.All(result.Coordinates, rectangle =>
        {
            Assert.True(rectangle.Left <= rectangle.Right);
            Assert.True(rectangle.Bottom <= rectangle.Top);
        });
    }

    [Fact]
    public void RepeatedMargins()
    {
        var results = Render(DocumentPreparationTestData.LayoutPages(2));
        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.Contains("PUBLICATION", result.ExcludedMargins);
            Assert.Contains("SIDE", result.ExcludedMargins);
            Assert.DoesNotContain("PUBLICATION", result.Sections);
            Assert.DoesNotContain("PUBLICATION", result.Entries);
            Assert.DoesNotContain("SIDE", result.Sections);
            Assert.DoesNotContain("SIDE", result.Entries);
            Assert.Contains("UNIQUE", result.Sections);
            Assert.InRange(result.RetainedWords, 1, result.SourceWords - 1);
        });
    }

    [Fact]
    public void SplitHeadingAndBoldRuns()
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.SplitHeading()));
        Assert.Contains("### 2 SYNTHETIC SECTION", result.Sections);
        Assert.DoesNotContain("### **", result.Sections);
        Assert.DoesNotContain("### 2\n", result.Sections);
        Assert.Contains("**Important product** description.", result.Sections);
        Assert.DoesNotContain("**Important** **product**", result.Sections);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ContentsRows(int columns, bool leadingReference)
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.Contents(columns, leadingReference)));
        var rows = result.Sections.Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(columns * 3, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.Contains($"**TOPIC {index / 3 + 1} {index % 3 + 1}**", rows[index]);
            var reference = (index + 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (leadingReference)
                Assert.StartsWith("- " + reference + " ", rows[index]);
            else
                Assert.EndsWith(" " + reference, rows[index]);
            Assert.DoesNotContain("....................", rows[index]);
        }
        Assert.Equal(result.SourceWords, result.RetainedWords);
        Assert.Equal(columns * 3 + 1, result.Coordinates.Count(rectangle => rectangle.View == "contents-row"));
        Assert.DoesNotContain(result.Coordinates, rectangle => rectangle.View == "section");
    }

    [Fact]
    public void ColumnOrderAndRowAssociations()
    {
        var columns = Assert.Single(Render(DocumentPreparationTestData.TwoColumns()));
        Assert.True(DocumentLayoutQuality.InOrder(columns.Sections.Replace("\\", "", StringComparison.Ordinal),
            ["LEFT first entry.", "LEFT second entry.", "RIGHT first entry.", "RIGHT second entry."]));
        var table = Assert.Single(Render(DocumentPreparationTestData.VocabularyTable()));
        Assert.Contains("die Tuer Die Tuer ist offen.", table.Entries);
        Assert.Contains("das Licht Das Licht ist aus.", table.Entries);
        Assert.False(DocumentLayoutQuality.InOrder(columns.Entries.Replace("\\", "", StringComparison.Ordinal),
            ["LEFT first entry.", "LEFT second entry.", "RIGHT first entry.", "RIGHT second entry."]),
            "Entry rows must remain labelled as ambiguous for independent columns.");
    }

    // Reported dictionary entries must retain their own wrapped examples rather than flatten into one paragraph.
    [Theory]
    [InlineData(180)]
    [InlineData(250)]
    public void DictionaryEntryBoundaries(double exampleLeft)
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.DictionaryEntries(exampleLeft)));
        Assert.Contains("the first, -s First example ends with a continuation!\n\n"
            + "second Second example. another example.\n\n"
            + "the third, -s Third example.", result.Sections);
        Assert.Equal(result.SourceWords, result.RetainedWords);
    }

    [Fact]
    public void SourceMarkupIsEscaped()
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.ReferenceLines("<script>", "![image](https://example.com/image)")));
        Assert.Contains("\\<script\\>", result.Sections);
        Assert.Contains("!\\[image\\](https://example.com/image)", result.Sections);
        Assert.DoesNotContain("<script>", result.Sections);
    }

    [Fact]
    public void SectionBandsAndDetachedMarkers()
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.BandedLists()));
        Assert.True(DocumentLayoutQuality.InOrder(result.Sections,
            ["FIRST GROUP", "First item.", "Second item.", "Third item.", "Fourth item.", "SECOND GROUP", "Later item.", "Final item."]));
        foreach (var text in new[] { "First", "Second", "Third", "Fourth", "Later", "Final" })
            Assert.Contains("- " + text + " item.", result.Sections);
        Assert.DoesNotContain("\n-\n", result.Sections);
        Assert.Equal(result.SourceWords, result.RetainedWords);
    }

    [Fact]
    public void UnicodeBullets()
    {
        // WinAnsi byte 0x95 is a bullet; the fixture writes Latin-1 bytes into a WinAnsi PDF font.
        var result = Assert.Single(Render(DocumentPreparationTestData.ReferenceLines("\u0095 First item.", "\u0095 Second item.")));
        Assert.Contains("- First item.", result.Sections);
        Assert.Contains("- Second item.", result.Sections);
        Assert.DoesNotContain('\u2022', result.Sections);
        Assert.Equal(result.SourceWords, result.RetainedWords);
    }

    [Theory]
    [InlineData("A 1-test sentence.", "A 1-test sentence.")]
    [InlineData("1. Source text.", "1\\. Source text.")]
    [InlineData("a) Plain example.", "a) Plain example.")]
    [InlineData("An (ordinary) phrase.", "An (ordinary) phrase.")]
    [InlineData("1) Source text.", "1\\) Source text.")]
    [InlineData("An ordinary exclamation!", "An ordinary exclamation!")]
    public void PunctuationAndListSyntax(string original, string expected)
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.ReferenceLines(original)));
        Assert.Contains(expected, result.Sections);
        Assert.DoesNotContain("\\-", result.Sections);
    }

    // Reported vocabulary/time examples must not flatten into prose or escape ordinary parentheses.
    [Theory]
    [InlineData("1 = one\n2 = two\n3 = three", "1 = one  \n2 = two  \n3 = three")]
    [InlineData("a) 2.30 = example\n3.40 = another\nb) one o'clock\nfive minutes (after one)\nhalf past two",
        "a) 2.30 = example  \n3.40 = another\n\nb) one o'clock  \nfive minutes (after one)  \nhalf past two")]
    [InlineData("term alpha\nterm beta\nterm gamma", "term alpha  \nterm beta  \nterm gamma")]
    [InlineData("amber\nbirch\ncedar", "amber  \nbirch  \ncedar")]
    [InlineData("the group              other group\nthe first              other first\nthe second\nthe third\nthe fourth",
        "the group other group  \nthe first other first  \nthe second  \nthe third  \nthe fourth")]
    [InlineData("An ordinary paragraph\nwraps across three\nshort lines.\nterm alpha\nterm beta\nterm gamma",
        "An ordinary paragraph wraps across three short lines.\n\nterm alpha  \nterm beta  \nterm gamma")]
    public void ExampleRowBoundaries(string original, string expected)
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.ExampleRows(original.Split('\n'))));
        Assert.Contains(expected, result.Sections);
        Assert.Equal(result.SourceWords, result.RetainedWords);
    }

    [Theory]
    [InlineData("An ordinary sentence with x = y\ncontinues as wrapped prose.",
        "An ordinary sentence with x = y continues as wrapped prose.")]
    [InlineData("An ordinary paragraph\nwraps across three\nshort lines.",
        "An ordinary paragraph wraps across three short lines.")]
    public void OrdinaryWrappedProse(string original, string expected)
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.ExampleRows(original.Split('\n'))));
        Assert.Contains(expected, result.Sections);
    }

    [Fact]
    public void BlankPage()
    {
        var result = Assert.Single(Render(DocumentPreparationTestData.Pdf("")));
        Assert.Empty(result.Sections);
        Assert.Empty(result.Entries);
        Assert.Empty(result.ExcludedMargins);
        Assert.Equal(0, result.SourceWords);
    }

    [Fact]
    public void Cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var pdf = PdfDocument.Open(DocumentPreparationTestData.Pdf("Synthetic."));
        var captured = DocumentLayoutReview.Capture(pdf.GetPage(1));
        Assert.Throws<OperationCanceledException>(() => DocumentLayoutReview.Render([captured], cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDuplicatedBlocksAreRejected(bool duplicate)
    {
        using var pdf = PdfDocument.Open(DocumentPreparationTestData.LayoutPages(1));
        var captured = DocumentLayoutReview.Capture(pdf.GetPage(1));
        var invalid = captured with { Blocks = duplicate ? captured.Blocks.Concat(captured.Blocks).ToArray() : [] };
        Assert.Throws<InvalidOperationException>(() => DocumentLayoutReview.Render([invalid], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrivateStructuredArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var original = DocumentPreparationTestData.LayoutPages(1);
        var layout = Assert.Single(Render(original));
        var prepared = new PdfPreparationResult
        {
            Status = PdfPreparationStatus.Prepared,
            Profile = new("Synthetic", "synthetic", "1", "1", 512, 64),
        };
        Directory.CreateDirectory(directory);
        try
        {
            await DocumentQualityReviewWriter.WriteSourceAsync(directory, "synthetic", original,
                [new(1, "baseline", "spatial", layout)], prepared, TestContext.Current.CancellationToken);
            foreach (var extension in new[] { "sections.md", "entries.md", "layout.json" })
                Assert.True(new FileInfo(Path.Combine(directory, $"synthetic.{extension}")).Length > 0);
            // The original baseline default was mistaken for a regression in the refined PDF view.
            var html = await File.ReadAllTextAsync(Path.Combine(directory, "synthetic.html"), TestContext.Current.CancellationToken);
            Assert.Contains("<option value=\"sections\" selected>", html);
            Assert.DoesNotContain("<option value=\"content\" selected>", html);
            Assert.Contains($"href=\"synthetic.sections.md\">Refined section Markdown ({DocumentLayoutReview.Version})</a>", html);
            Assert.Contains("href=\"synthetic.md\">Original baseline Markdown</a>", html);
            var sections = await File.ReadAllTextAsync(Path.Combine(directory, "synthetic.sections.md"), TestContext.Current.CancellationToken);
            Assert.Contains("## PDF page 1", sections);
            Assert.Contains("### 1 OVERVIEW", sections);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "synthetic.layout.json"), TestContext.Current.CancellationToken);
            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(layout.SourceWords, parsed.RootElement[0].GetProperty("Layout").GetProperty("SourceWords").GetInt32());
            Assert.Equal(DocumentLayoutReview.Version, parsed.RootElement[0].GetProperty("LayoutAlgorithm").GetString());
        }
        finally
        {
            foreach (var extension in new[] { "pdf", "html", "md", "sections.md", "entries.md", "layout.json" })
                File.Delete(Path.Combine(directory, $"synthetic.{extension}"));
            Directory.Delete(directory);
        }
    }

    private static IReadOnlyList<DocumentLayoutReview.Result> Render(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        var captured = Enumerable.Range(1, pdf.NumberOfPages).Select(number => DocumentLayoutReview.Capture(pdf.GetPage(number))).ToArray();
        return DocumentLayoutReview.Render(captured, TestContext.Current.CancellationToken);
    }
}
#endif
