using CasCap.Client.Models;
using CasCap.Client.Pages;
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
        Assert.Contains("ReviewRequired", html, StringComparison.Ordinal);
        Assert.Contains("Stored snapshot", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("not everything in Blob Storage", html, StringComparison.Ordinal);
        Assert.Contains(handler.Requests, request => request.Contains("collection=manuals", StringComparison.Ordinal));
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
        Assert.Single(handler.Requests);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            if (Failure is { } failure)
                return Task.FromResult(new HttpResponseMessage(failure));
            object value = request.RequestUri.AbsolutePath.EndsWith("/documents", StringComparison.Ordinal)
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
                        SourceState = "Queued",
                        JobId = Job,
                        JobState = "ReviewRequired",
                        CanReview = true,
                        StoredGenerationId = "example-generation",
                    }],
                }
                : Review();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
        }

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
