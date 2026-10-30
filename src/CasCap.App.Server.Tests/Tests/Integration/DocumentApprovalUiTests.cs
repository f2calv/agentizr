using CasCap.Client.Components;
using CasCap.Client.Models;
using static CasCap.Tests.Integration.AdministrationComponentTestRenderer;

namespace CasCap.Tests.Integration;

/// <summary>Renders readiness form states and escaped assessment metadata without mutations or live services.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentReviewManagement")]
public sealed class DocumentApprovalUiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AssessmentForm_RequiresSavedSelectionAndEscapesMetadata(bool selected, bool saved)
    {
        using var handler = new ReadOnlyHandler();
        var review = DocumentApprovalTestData.Review(selected, saved, current: true, approved: false);
        var html = await RenderAsync<DocumentApproval>(handler, new() { [nameof(DocumentApproval.Review)] = review }, string.Empty);
        Assert.Contains("Document-level approval", html, StringComparison.Ordinal);
        Assert.Equal(selected, html.Contains("Save document assessment", StringComparison.Ordinal));
        if (!selected)
            Assert.Contains("Save a chunk review", html, StringComparison.Ordinal);
        if (selected)
        {
            Assert.Contains("No checkbox can bypass", html, StringComparison.Ordinal);
            Assert.Contains("Highest reviewed CEFR", html, StringComparison.Ordinal);
        }
        if (saved)
        {
            Assert.Contains("&lt;script&gt;review", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>review", html, StringComparison.Ordinal);
        }
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(true, true, "Readiness approved; indexing blocked")]
    [InlineData(true, false, "Approval blocked or withheld")]
    [InlineData(false, false, "Stale assessment; reassessment required")]
    public async Task AssessmentStatus_IsSeparateFromIndexing(bool current, bool approved, string status)
    {
        using var handler = new ReadOnlyHandler();
        var html = await RenderAsync<DocumentApproval>(handler,
            new() { [nameof(DocumentApproval.Review)] = DocumentApprovalTestData.Review(true, true, current, approved) }, string.Empty);
        Assert.Contains(status, html, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AssessmentForm_ShowsServerFieldValidation()
    {
        using var handler = new ReadOnlyHandler();
        var html = await RenderAsync<DocumentApproval>(handler, new()
        {
            [nameof(DocumentApproval.Review)] = DocumentApprovalTestData.Review(true, false, false, false),
            [nameof(DocumentApproval.FieldErrors)] = new Dictionary<string, string[]> { ["notes"] = ["Example server field error"] },
        }, string.Empty);
        Assert.Contains("Example server field error", html, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class ReadOnlyHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("The standalone assessment form must not make HTTP requests.");
        }
    }
}
