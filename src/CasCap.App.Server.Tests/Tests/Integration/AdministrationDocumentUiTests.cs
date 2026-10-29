using CasCap.Client.Models;
using CasCap.Client.Pages;
using CasCap.Client.Constants;
using CasCap.Client.Services;
using System.Text.Json;
using static CasCap.Tests.Integration.AdministrationComponentTestRenderer;

namespace CasCap.Tests.Integration;

/// <summary>Checks catalogue and review states, escaped document content and read-only rendering over synthetic HTTP.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentReviewManagement")]
public sealed class AdministrationDocumentUiTests
{
    private const string App = "app-a";
    private const string Collection = "manuals";
    private const string Document = "example-document";
    private const string Job = "example-job";

    [Fact]
    public async Task Catalogue_UsesTypedReadsAndEscapesSourceNames()
    {
        using var handler = new Handler();
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, "?collection=manuals");
        Assert.Contains("Documents in app-a", html, StringComparison.Ordinal);
        Assert.Contains("Ready for review", html, StringComparison.Ordinal);
        Assert.Contains("Stored snapshot", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("not everything in Blob Storage", html, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, request => request.Contains("collection=manuals", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.EndsWith("/apps/app-a/documents/synchronization", StringComparison.Ordinal));
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_SeparatesNotesAndEscapesMarkdown(bool notes)
    {
        using var handler = new Handler { Notes = notes };
        var html = await RenderAsync<DocumentReview>(handler, Parameters(), string.Empty);
        Assert.Contains("Store review-required snapshot", html, StringComparison.Ordinal);
        Assert.Contains("markdown-headings", html, StringComparison.Ordinal);
        Assert.Contains("# Root", html, StringComparison.Ordinal);
        Assert.Contains("## Child", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Equal(notes, html.Contains("Page uncertainties (not embedded)", StringComparison.Ordinal));
        Assert.Equal(notes, html.Contains("Preparation changes (not embedded)", StringComparison.Ordinal));
        Assert.Contains("not PDF regions", html, StringComparison.Ordinal);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoredSnapshot_UsesExactReadAndCannotResubmit()
    {
        using var handler = new Handler { Generation = "example-generation" };
        var html = await RenderAsync<DocumentReview>(handler, Parameters(), "?generationId=example-generation");
        Assert.Contains("example-generation", html, StringComparison.Ordinal);
        Assert.Contains("Current default-profile preview", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Store review-required snapshot", html, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, request => request.EndsWith("/review/generations/example-generation", StringComparison.Ordinal));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Sign in to administer this app")]
    [InlineData(HttpStatusCode.Forbidden, "not assigned")]
    [InlineData(HttpStatusCode.NotFound, "Nothing was found")]
    [InlineData(HttpStatusCode.Conflict, "current revision changed")]
    [InlineData(HttpStatusCode.NotImplemented, "does not support this operation")]
    public async Task Review_RendersExplicitFailure(HttpStatusCode failure, string text)
    {
        using var handler = new Handler { Failure = failure };
        var html = await RenderAsync<DocumentReview>(handler, Parameters(), string.Empty);
        Assert.Contains(text, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Store review-required snapshot", html, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedProjection_DisablesStorage()
    {
        using var handler = new Handler { InvalidProjection = true };
        var html = await RenderAsync<DocumentReview>(handler, Parameters(), string.Empty);
        Assert.Contains("projection format is unsupported", html, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*disabled[^>]*>[\\s\\S]*?Store review-required snapshot", html);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EmptyCatalogue_IsNotAnUploadSuccess()
    {
        using var handler = new Handler { Empty = true };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains("No admitted documents", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Upload<", html, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public async Task SynchronizationControlRequiresEnabledMappedSource(bool enabled, bool mapped, bool disabled)
    {
        using var handler = new Handler { SynchronizationEnabled = enabled, SynchronizationMapped = mapped };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, "?collection=filtered");
        Assert.Equal(disabled, System.Text.RegularExpressions.Regex.IsMatch(html,
            "<button[^>]*disabled[^>]*>[\\s\\S]*?Sync documents"));
        Assert.Contains("all configured collections", html, StringComparison.Ordinal);
        Assert.Contains("Preview preparation is paused", html, StringComparison.Ordinal);
        Assert.Contains("does not prepare previews", html, StringComparison.Ordinal);
        if (!enabled)
            Assert.Contains("synchronization is disabled", html, StringComparison.Ordinal);
        if (!mapped)
            Assert.Contains("No source containers", html, StringComparison.Ordinal);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SynchronizationProgressAndConflictsAreExplicitAndEscaped()
    {
        using var handler = new Handler { SynchronizationConflict = true };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains("Last successful scan", html, StringComparison.Ordinal);
        Assert.Contains("PDFs added: 1", html, StringComparison.Ordinal);
        Assert.Contains("Work items repaired: 2", html, StringComparison.Ordinal);
        Assert.Contains("Missing PDFs removed: 3", html, StringComparison.Ordinal);
        Assert.Contains("Unchanged: 4", html, StringComparison.Ordinal);
        Assert.Contains("Conflicts: 5", html, StringComparison.Ordinal);
        Assert.Contains("does not mean every discrepancy", html, StringComparison.Ordinal);
        Assert.Contains("StorageUnavailable", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Sign in to administer this app")]
    [InlineData(HttpStatusCode.Forbidden, "not assigned")]
    [InlineData(HttpStatusCode.NotImplemented, "does not support this operation")]
    public async Task SynchronizationFailureDoesNotHideTheCatalogue(HttpStatusCode failure, string text)
    {
        using var handler = new Handler { SynchronizationFailure = failure };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains(text, html, StringComparison.Ordinal);
        Assert.Contains("Stored snapshot", html, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*disabled[^>]*>[\\s\\S]*?Sync documents", html);
    }

    [Fact]
    public async Task SynchronizationClientDoesNotRetryAnUnknownMutationOutcome()
    {
        using var handler = new Handler { MutationFailure = HttpStatusCode.ServiceUnavailable };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var session = new AdministrationSessionState
        {
            Current = new() { IsAuthenticated = true, SignInRequired = true, AntiforgeryToken = "synthetic-token" },
        };
        var client = new AdministrationApiClient(http, session);
        var result = await client.RequestDocumentSynchronizationAsync(App, TestContext.Current.CancellationToken);
        Assert.Equal(ApiStatus.Unavailable, result.Status);
        Assert.StartsWith("POST ", Assert.Single(handler.Requests), StringComparison.Ordinal);
        Assert.True(handler.MutationWasProtected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Catalogue_DistinguishesStorageScanFromPreviewPreparation(bool processing, bool configured)
    {
        // Regression: a successful storage scan must not imply the queued PDF has been prepared.
        using var handler = new Handler
        {
            ProcessingEnabled = processing,
            PreviewConfigured = configured,
            JobState = "Queued",
        };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains("manuals: Storage scan complete", html, StringComparison.Ordinal);
        Assert.DoesNotContain("manuals: Succeeded", html, StringComparison.Ordinal);
        Assert.Contains("does not mean document previews have been prepared", html, StringComparison.Ordinal);
        Assert.Contains(configured ? "Waiting for preparation" : "Preparation setup required", html, StringComparison.Ordinal);
        Assert.Contains("Not prepared yet", html, StringComparison.Ordinal);
        Assert.Equal(!processing, html.Contains("Preview preparation is paused", StringComparison.Ordinal));
        Assert.Equal(processing, html.Contains("Document processing is enabled", StringComparison.Ordinal));
        Assert.Matches("<details[^>]*>\\s*<summary[^>]*>Technical details</summary>[\\s\\S]*?example-document[\\s\\S]*?</details>", html);
        Assert.DoesNotMatch("<details[^>]*\\bopen\\b", html);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NotRun", "Storage not yet scanned")]
    [InlineData("Queued", "Storage scan queued")]
    [InlineData("Running", "Storage scan in progress")]
    [InlineData("Succeeded", "Storage scan complete")]
    [InlineData("Failed", "Storage scan failed")]
    [InlineData("Unexpected", "Unrecognized storage scan state: Unexpected")]
    public async Task Synchronization_LabelsOnlyStorageProgress(string state, string expected)
    {
        using var handler = new Handler { SynchronizationState = state };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains(expected, html, StringComparison.Ordinal);
        Assert.Contains("Every hour (UTC)", html, StringComparison.Ordinal);
        Assert.Contains("2. Prepare document previews", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Processing", "Queued", "Preparing document")]
    [InlineData("RetryPending", "Queued", "Waiting to retry")]
    [InlineData("ReviewRequired", "Queued", "Ready for review")]
    [InlineData("Completed", "Queued", "Processing complete")]
    [InlineData("Failed", "Queued", "Preparation failed")]
    [InlineData("ReconciliationRequired", "Queued", "Operator reconciliation required")]
    [InlineData("Ignored", "Queued", "Superseded job")]
    [InlineData(null, "Queued", "No current preparation job")]
    [InlineData("Unexpected", "Queued", "Unrecognized preparation state: Unexpected")]
    [InlineData("Queued", "Deleted", "Original removed")]
    public async Task Catalogue_LabelsPreparationStates(string? state, string sourceState, string expected)
    {
        using var handler = new Handler { JobState = state, SourceState = sourceState };
        var html = await RenderAsync<Documents>(handler, new() { [nameof(Documents.AppId)] = App }, string.Empty);
        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Parameters() => new()
    {
        [nameof(DocumentReview.AppId)] = App,
        [nameof(DocumentReview.CollectionId)] = Collection,
        [nameof(DocumentReview.DocumentId)] = Document,
        [nameof(DocumentReview.JobId)] = Job,
    };

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public bool Notes { get; init; }

        public bool Empty { get; init; }

        public bool InvalidProjection { get; init; }

        public string? Generation { get; init; }

        public HttpStatusCode? Failure { get; init; }

        public HttpStatusCode? SynchronizationFailure { get; init; }

        public bool SynchronizationEnabled { get; init; } = true;

        public bool SynchronizationMapped { get; init; } = true;

        public bool SynchronizationConflict { get; init; }

        public string SynchronizationState { get; init; } = "Succeeded";

        public bool ProcessingEnabled { get; init; }

        public string? JobState { get; init; } = "ReviewRequired";

        public string SourceState { get; init; } = "Queued";

        public bool PreviewConfigured { get; init; } = true;

        public HttpStatusCode? MutationFailure { get; init; }

        public bool MutationWasProtected { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            if (request.Method == HttpMethod.Post && MutationFailure is { } mutationFailure)
            {
                MutationWasProtected = request.Headers.Contains(AdministrationRoutes.AntiforgeryHeaderName);
                return Task.FromResult(new HttpResponseMessage(mutationFailure));
            }
            if (Failure is { } failure)
                return Task.FromResult(new HttpResponseMessage(failure));
            var synchronization = request.RequestUri.AbsolutePath.EndsWith("/synchronization", StringComparison.Ordinal);
            if (synchronization && SynchronizationFailure is { } syncFailure)
                return Task.FromResult(new HttpResponseMessage(syncFailure));
            object value = synchronization ? Synchronization() : request.RequestUri.AbsolutePath.EndsWith("/documents", StringComparison.Ordinal)
                ? new DocumentCataloguePageResponse
                {
                    Page = 1,
                    PageSize = 25,
                    TotalCount = Empty ? 0 : 1,
                    Items = Empty ? [] : [new()
                    {
                        DocumentId = Document,
                        CollectionId = Collection,
                        FileName = "<img src=x onerror=alert(1)>.pdf",
                        SourceState = SourceState,
                        JobId = Job,
                        JobState = JobState,
                        PreviewConfigured = PreviewConfigured,
                        CanReview = JobState == "ReviewRequired" && SourceState != "Deleted",
                        StoredGenerationId = JobState == "ReviewRequired" ? "example-generation" : null,
                    }],
                }
                : Review();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
        }

        private DocumentSynchronizationResponse Synchronization() => new()
        {
            Enabled = SynchronizationEnabled,
            ProcessingEnabled = ProcessingEnabled,
            Cron = "0 * * * *",
            Sources = !SynchronizationMapped ? [] :
            [
                new()
                {
                    SourceId = "synthetic-source",
                    CollectionId = SynchronizationConflict ? "<script>" : Collection,
                    State = SynchronizationConflict ? "Failed" : SynchronizationState,
                    AddedCount = 1,
                    RepairedCount = 2,
                    RemovedCount = 3,
                    UnchangedCount = 4,
                    ConflictCount = SynchronizationConflict ? 5 : 0,
                    FailureCode = SynchronizationConflict ? "StorageUnavailable" : null,
                    LastSucceededAtUtc = DateTimeOffset.UnixEpoch,
                },
            ],
        };

        private DocumentReviewGenerationResponse Review() => new()
        {
            State = "ReviewRequired",
            GenerationId = Generation,
            Sequencer = "0001",
            ETag = "\"example-etag\"",
            SourceSha256 = new('0', 64),
            Pages = [new()
            {
                Page = 6,
                Markdown = "# Root\n## Child\n\n<script>alert(1)</script>",
                Uncertainties = Notes ? ["example uncertainty"] : [],
                Changes = Notes ? ["example change"] : [],
            }],
            Projection = InvalidProjection ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(new
            {
                profile = new
                {
                    strategy = "markdown-headings",
                    targetTokensPerChunk = 512,
                    maximumTokensPerChunk = 512,
                    overlapTokens = 64,
                },
                chunks = new[]
                {
                    new
                    {
                        ordinal = 0,
                        tokenCount = 20,
                        embeddingText = "# Root\n## Child\n\n<script>alert(1)</script>",
                        sourceSpans = new[] { new { pageNumber = 6, startOffset = 16, endOffset = 41, blockOrdinal = 2 } },
                    },
                },
            }),
        };
    }
}
