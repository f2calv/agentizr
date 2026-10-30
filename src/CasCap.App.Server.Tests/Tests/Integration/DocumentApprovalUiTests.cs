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
        Assert.Contains("Document assessment and readiness", html, StringComparison.Ordinal);
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
            Assert.Contains("Assessment saved", html, StringComparison.Ordinal);
            Assert.Contains("Record a new assessment", html, StringComparison.Ordinal);
            Assert.Contains("your saved assessment has not been erased", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;review", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>review", html, StringComparison.Ordinal);
        }
        else
            Assert.Contains("No document assessment has been saved", html, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(true, true, "Readiness: approved.")]
    [InlineData(true, false, "Readiness: blocked despite the saved approval request.")]
    [InlineData(false, false, "Readiness: stale assessment; reassessment required.")]
    public async Task AssessmentStatus_IsSeparateFromIndexing(bool current, bool approved, string status)
    {
        using var handler = new ReadOnlyHandler();
        var html = await RenderAsync<DocumentApproval>(handler,
            new() { [nameof(DocumentApproval.Review)] = DocumentApprovalTestData.Review(true, true, current, approved) }, string.Empty);
        Assert.Contains(status, html, StringComparison.Ordinal);
        Assert.Contains("Assessment saved", html, StringComparison.Ordinal);
        Assert.Contains("Indexing authorization: not granted by this assessment", html, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedAttestationsRemainVisibleBesideFreshAssessment(bool requested)
    {
        using var handler = new ReadOnlyHandler();
        var review = DocumentApprovalTestData.Review(true, true, current: true, approved: false);
        review = review with { Approval = review.Approval! with { Assessment = review.Approval.Assessment with { ApprovalRequested = requested } } };
        var html = await RenderAsync<DocumentApproval>(handler, new() { [nameof(DocumentApproval.Review)] = review }, string.Empty);
        Assert.Contains("Last saved assessment", html, StringComparison.Ordinal);
        Assert.Contains("Saved assessment values", html, StringComparison.Ordinal);
        Assert.Contains("Original-source and page provenance checked", html, StringComparison.Ordinal);
        Assert.Contains("Included content checked", html, StringComparison.Ordinal);
        Assert.Contains("Safety-critical content checked", html, StringComparison.Ordinal);
        Assert.Contains("Yes (saved)", html, StringComparison.Ordinal);
        Assert.Contains(requested ? "blocked despite the saved approval request" : "approval withheld by the reviewer", html, StringComparison.Ordinal);
        Assert.Contains("Example unresolved gate", html, StringComparison.Ordinal);
        Assert.Contains("Checkboxes and rationale intentionally start empty", html, StringComparison.Ordinal);
        Assert.Contains("Record a new assessment", html, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SavedManualAssessmentShowsSuccessfulPersistenceAndVisualBlocker()
    {
        using var handler = new ReadOnlyHandler();
        var review = DocumentApprovalTestData.Review(true, true, current: true, approved: false);
        var assessment = review.Approval!.Assessment with { DocumentType = CasCap.Client.Constants.DocumentApprovalValues.DeviceManual, CefrLevel = null };
        review = review with { Approval = review.Approval with { Assessment = assessment, Blockers = assessment.Blockers(1) } };
        var html = await RenderAsync<DocumentApproval>(handler, new() { [nameof(DocumentApproval.Review)] = review }, string.Empty);
        Assert.Contains("Assessment saved", html, StringComparison.Ordinal);
        Assert.Contains("Readiness: blocked despite the saved approval request.", html, StringComparison.Ordinal);
        Assert.Contains("Manual figure/caption/source associations", html, StringComparison.Ordinal);
        Assert.Contains("No checkbox can bypass", html, StringComparison.Ordinal);
        Assert.Contains("Indexing authorization: not granted", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Readiness: approved.", html, StringComparison.Ordinal);
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
