#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;

namespace CasCap.TestData;

internal static class DocumentIndexingTestData
{
    public static DocumentAzureEmbeddingConfig Azure => new()
    {
        Endpoint = "https://embedding.example.com/",
        DeploymentName = "example-deployment",
        DeploymentResourceId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/example/providers/Microsoft.CognitiveServices/accounts/example/deployments/example-deployment",
        ManagementEndpoint = "https://management.azure.com/",
    };

    public static DocumentIndexingConfig ExecutionConfig(string tenantId = "tenant-a", string collectionId = "collection-a")
    {
        var config = Config(tenantId, collectionId);
        return config with
        {
            ProcessingEnabled = true,
            Profiles = [config.Profiles[0] with { Model = "text-embedding-3-small", Azure = Azure, MaximumBatchInputs = 1 }],
        };
    }

    public static DocumentIndexingConfig Config(string tenantId = "tenant-a", string collectionId = "collection-a")
    {
        var tokenizer = new DocumentChunkingService().GetMarkdownProfile(new());
        return new()
        {
            Enabled = true,
            Profiles = [new()
            {
                Id = "example-embedding",
                Provider = "example-provider",
                Model = "example-embedding-model",
                ModelRevision = "example-revision",
                Dimensions = 8,
                DistanceMetric = "cosine",
                TokenizerEncoding = tokenizer.TokenizerEncoding,
                TokenizerVersion = tokenizer.TokenizerVersion,
                MaximumTokensPerInput = 512,
                MaximumChunks = 5_000,
                MaximumTotalTokens = 100_000,
            }],
            Collections = [new()
            {
                TenantId = tenantId,
                CollectionId = collectionId,
                Revision = 1,
                DocumentType = DocumentApprovalValues.Vocabulary,
                Language = "de",
                MaximumCefrLevel = "A2",
                ProfileId = "example-embedding",
            }],
        };
    }

    public static DocumentIndexingConfig OllamaConfig()
    {
        var config = Config();
        return config with
        {
            ProcessingEnabled = true,
            Profiles = [config.Profiles[0] with
            {
                Model = "qwen3-embedding:4b",
                ModelRevision = new('a', 64),
                Ollama = new() { Endpoint = "http://ollama.example.com:11434/" },
            }],
        };
    }

    public static DocumentIndexingReview Review()
    {
        var source = new DocumentIngestionClaim
        {
            TenantId = "tenant-a",
            CollectionId = "collection-a",
            DocumentId = "example-document",
            JobId = "example-job",
            ClaimId = Guid.Empty,
            Sequencer = "0001",
            ETag = "\"example-etag\"",
            BlobUri = "https://storage.example.com/documents/example.pdf",
            ExpiresAtUtc = DateTimeOffset.UnixEpoch,
        };
        var body = string.Join(' ', Enumerable.Repeat("Beispiel", 180));
        var projection = new DocumentChunkingService().ChunkMarkdown(
            [new(1, "# Wortschatz\n\n" + body)], new() { MaximumTokensPerChunk = 32, TargetTokensPerChunk = 24, OverlapTokens = 0 },
            new(), CancellationToken.None);
        return new(source, "example-generation", new(1, new('1', 64), new('2', 64), [], [], projection),
            new() { Revision = 1, RecordedAtUtc = DateTimeOffset.UnixEpoch, Exclusions = [new() { Ordinal = 0 }] },
            new()
            {
                Revision = 1,
                RecordedAtUtc = DateTimeOffset.UnixEpoch,
                IsCurrent = true,
                Blockers = [],
                Assessment = DocumentApprovalTestData.Request(),
            });
    }
}
#endif
