#if DEBUG
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CasCap.Constants;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CasCap.Tests;

/// <summary>Explicit synthetic hybrid authorization and fake source transport; no external configuration or network.</summary>
internal static class DocumentHybridPreparationTestData
{
    public static DocumentPreparationPlan Plan => new()
    {
        TenantId = "tenant-a",
        Definition = Definition,
        DefinitionSha256 = DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(Definition, JsonSerializerOptions.Web)),
        Settings = new()
        {
            AgentName = "document-preparation",
            DefinitionVersion = DocumentPreparationAgentTemplate.Version,
            Model = "synthetic-model",
            ModelRevision = "synthetic-revision",
            DeploymentResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/example/providers/Microsoft.CognitiveServices/accounts/example/deployments/synthetic-deployment",
            ManagementEndpoint = "https://management.azure.com/",
            DocumentIntelligenceEndpoint = "https://layout.example.com/",
            MaximumPages = 2,
            MaximumOutputTokens = 8192,
            RequestTimeoutSeconds = 30,
        },
    };

    public static DocumentBlobDownloadService Downloader(Func<Stream> source, DocumentIngestionConfig config) =>
        new(Options.Create(config), _ => new TestStorage(new SourceContainer(source)));

    public static DocumentPreparationRequestIdentity LayoutIdentity(DocumentIngestionClaim claim, byte[] pdf)
    {
        var source = DocumentPreparationIdentity.Hash(pdf);
        var plan = DocumentPreparationIdentity.Hash(claim.PreparationPlanJson ?? string.Empty);
        return new(source, plan, DocumentPreparationIdentity.Digest(source, plan, AzDocumentLayoutClient.ApiVersion,
            AzDocumentLayoutClient.ModelId, "markdown", "utf16CodeUnit", "1"), DocumentPreparationOperations.DocumentIntelligence, 0);
    }

    internal sealed class Provider : IDocumentPreparationProvider
    {
        public int Submissions { get; private set; }
        public int Polls { get; private set; }
        public int ModelCalls { get; private set; }
        public int[]? SubmittedPages { get; private set; }
        public string? SubmittedSourceSha256 { get; private set; }
        public bool FailSubmission { get; set; }
        public bool FailModel { get; set; }
        public bool FailVerification { get; set; }
        public string? Text { get; set; }
        public Func<AgentStructuredResponse, AgentStructuredResponse>? ResponseChange { get; set; }
        public Action? BeforeResponse { get; set; }

        public Task VerifyModelAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken) =>
            FailVerification ? Task.FromException(new InvalidDataException("Synthetic model revision mismatch.")) : Task.CompletedTask;

        public Task<Uri> SubmitLayoutAsync(DocumentPreparationPlan plan, byte[] pdf, int[] pages, CancellationToken cancellationToken)
        {
            Submissions++;
            SubmittedPages = pages.ToArray();
            SubmittedSourceSha256 = DocumentPreparationIdentity.Hash(pdf);
            return FailSubmission ? Task.FromException<Uri>(new HttpRequestException("Synthetic unknown submission."))
                : Task.FromResult(new Uri("https://layout.example.com/operation"));
        }

        public Task<DocumentLayoutPollResult> PollLayoutAsync(DocumentPreparationPlan plan, Uri operation, int[] pages, CancellationToken cancellationToken)
        {
            Polls++;
            return Task.FromResult(new DocumentLayoutPollResult
            {
                Status = "Succeeded",
                Markdown = "Managed draft",
                Pages = pages.ToDictionary(page => page, _ => "Managed draft"),
            });
        }

        public async Task ExecutePageAsync(DocumentPreparationPageRequest request,
            Func<AgentStructuredResponse, CancellationToken, ValueTask> observer, CancellationToken cancellationToken)
        {
            ModelCalls++;
            if (FailModel)
                throw new HttpRequestException("Synthetic unknown model submission.");
            var response = new AgentStructuredResponse
            {
                Text = Text ?? JsonSerializer.Serialize(new
                {
                    page = request.PageNumber,
                    markdown = "Keep <think>source text</think>.",
                    changes = Array.Empty<string>(),
                    uncertainties = Array.Empty<string>(),
                }),
                ModelId = "synthetic-model",
                ResponseId = "resp_synthetic",
                IsComplete = true,
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
            };
            BeforeResponse?.Invoke();
            await observer(ResponseChange?.Invoke(response) ?? response, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(request.Pdf);
            Assert.Equal(1, pdf.NumberOfPages);
            if (request.Plan.Settings.PhysicalPages.Length == 0)
                Assert.InRange(request.PageNumber, 1, request.Plan.Settings.MaximumPages);
            else
                Assert.Contains(request.PageNumber, request.Plan.Settings.PhysicalPages);
            Assert.Equal("Managed draft", request.Draft);
        }
    }

    private static AgentDefinition Definition => new()
    {
        Name = "document-preparation",
        Version = DocumentPreparationAgentTemplate.Version,
        Agent = DocumentPreparationAgentTemplate.Create().Definition.GetProperty("agent")
            .Deserialize<AgentConfig>(JsonSerializerOptions.Web)! with
        {
            Provider = "synthetic-provider",
        },
        Provider = new()
        {
            Type = AgentType.AzureOpenAI,
            Endpoint = new Uri("https://example.com/"),
            ModelName = "synthetic-deployment",
            UseResponsesApi = true,
        },
    };

    private sealed class TestStorage(BlobContainerClient client) : AzBlobStorageBase(client);

    private sealed class SourceContainer(Func<Stream> source) : BlobContainerClient
    {
        public override string Name => "documents";
        public override BlobClient GetBlobClient(string blobName) => new SourceBlob(source);
    }

    private sealed class SourceBlob(Func<Stream> source) : BlobClient
    {
        public override Task<Stream> OpenReadAsync(BlobOpenReadOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(source());
    }
}
#endif
