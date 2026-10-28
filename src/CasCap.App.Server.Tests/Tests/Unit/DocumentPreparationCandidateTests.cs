#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Protects document Markdown from review-only annotations and extraction spacing artifacts.</summary>
[Trait("Category", "HybridPreparation")]
public sealed class DocumentPreparationCandidateTests
{
    private static readonly string[] ReviewChanges = ["Review only"];

    [Theory]
    [InlineData("Word&nbsp;word", "Word word")]
    [InlineData("Word&#160;word", "Word word")]
    [InlineData("Word&#xA0;word", "Word word")]
    [InlineData("Word\u00a0word\u2007word\u202fword", "Word word word word")]
    [InlineData("Word&ensp;word", "Word\u2002word")]
    [InlineData("Word&#x20;word", "Word word")]
    [InlineData("# Title\r\n\r\n- **Text**", "# Title\n\n- **Text**")]
    [InlineData("Literal &amp; &lt;script&gt; `code` **bold**", "Literal &amp; &lt;script&gt; `code` **bold**")]
    public void MarkdownWhitespace(string markdown, string expected)
    {
        // Review reported extraction entities in Markdown; raw receipts must remain unchanged.
        var response = new AgentStructuredResponse
        {
            Text = JsonSerializer.Serialize(new { page = 1, markdown, changes = ReviewChanges, uncertainties = Array.Empty<string>() }),
            ResponseId = "synthetic",
            ModelId = DocumentHybridPreparationTestData.Plan.Settings.Model,
            IsComplete = true,
        };
        var candidate = DocumentPreparationCandidateValidator.Validate(
            response, DocumentHybridPreparationTestData.Plan, 1, 10_000);
        Assert.Equal(expected, candidate.Markdown);
        Assert.Equal(["Review only"], candidate.Changes);
        Assert.Empty(candidate.Uncertainties);
        Assert.Equal(markdown, JsonSerializer.Deserialize<JsonElement>(response.Text).GetProperty("markdown").GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReviewMetadata(bool changes, bool uncertainties)
    {
        // Review headings and token counts must not be presented as part of the transcription.
        var candidate = new DocumentPreparationCandidate(1, "# Source\nLiteral <script> text",
            changes ? ["Corrected <draft>"] : ["", " "],
            uncertainties ? ["Unclear <word>"] : ["", "\t"]);
        var html = DocumentPreparationReviewRenderer.Render(candidate);
        Assert.StartsWith("<pre class='transcription'># Source\nLiteral &lt;script&gt; text</pre>", html, StringComparison.Ordinal);
        Assert.Equal(uncertainties, html.Contains("<h4>Page Uncertainties</h4>", StringComparison.Ordinal));
        Assert.Equal(changes, html.Contains("<details class='review-metadata'>", StringComparison.Ordinal));
        Assert.DoesNotContain("Native usage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h4>Changes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        if (changes)
            Assert.Contains("Corrected &lt;draft&gt;", html, StringComparison.Ordinal);
        if (uncertainties)
            Assert.Contains("Unclear &lt;word&gt;", html, StringComparison.Ordinal);
    }
}
#endif
