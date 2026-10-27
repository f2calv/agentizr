#if DEBUG
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CasCap.Constants;
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
            DefinitionVersion = "PdfReconciliationV1",
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

    private static AgentDefinition Definition => new()
    {
        Name = "document-preparation",
        Version = "PdfReconciliationV1",
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
