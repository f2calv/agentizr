#if DEBUG
using Azure.Storage.Blobs;
using CasCap.Common.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Constants;
using CasCap.Entities;
using CasCap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;

namespace CasCap.Tests.Integration;

/// <summary>Opt-in real-document acceptance through the production worker/provider with a retained, local-only paid ledger.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "LiveDocumentPreparation")]
public sealed class DocumentPreparationLiveAcceptanceTests(ITestOutputHelper output)
{
    private const string ProviderName = "acceptance-preparation";
    private const int PageBudget = 3;
    private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerOptions.Web) { WriteIndented = true };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Gets the explicit operator gate; normal regression runs never authorize paid requests.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("CASCAP_RUN_DOCUMENT_PREPARATION_ACCEPTANCE") == "true";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires separate approval, exact source/model budgets and a fresh retained local ledger.")]
    public async Task BoundedProductionPreparation()
    {
        var root = Required("CASCAP_DOCUMENT_ACCEPTANCE_ROOT");
        var directory = Required("CASCAP_DOCUMENT_ACCEPTANCE_OUTPUT");
        var selections = JsonSerializer.Deserialize<Dictionary<string, int[]>>(Required("CASCAP_DOCUMENT_ACCEPTANCE_PAGES"))
            ?? throw new InvalidOperationException("Explicit source selections are required.");
        var metadata = JsonSerializer.Deserialize<AzureMetadata>(Required("CASCAP_DOCUMENT_ACCEPTANCE_METADATA"), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Read-only Azure deployment metadata is required.");
        Assert.Equal(2, selections.Count);
        Assert.All(selections.Values, pages => Assert.Equal(PageBudget, pages.Length));
        Assert.True(Path.IsPathFullyQualified(root) && Path.IsPathFullyQualified(directory));
        Assert.False(Directory.Exists(directory), "Use a fresh private review directory, never overwrite paid-run evidence.");

        var connection = new NpgsqlConnectionStringBuilder(Required("ConnectionStrings__PostgreSql"));
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.Equal(5432, connection.Port);
        Assert.StartsWith("agentizr_acceptance_", connection.Database, StringComparison.Ordinal);
        var factory = new PooledDbContextFactory<AgentRuntimeDbContext>(
            new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await using (var db = factory.CreateDbContext())
            Assert.False(await db.Database.CanConnectAsync(Token), "The paid ledger must be fresh. Inspect retained receipts instead of resubmitting.");

        using var configuration = new ConfigurationManager();
        configuration.AddConfiguration(new ConfigurationBuilder()
            .AddStandardConfiguration("Development", typeof(RuntimeEntraCredential).Assembly).SetBasePath(root).Build(),
            shouldDisposeConfiguration: true);
        var credential = configuration.AddRuntimeKeyVaultConfiguration();
        Assert.NotNull(credential.Credential);
        var configuredRuntime = configuration.GetSection(AgentRuntimeConfig.ConfigurationSectionName).Get<AgentRuntimeConfig>()
            ?? throw new InvalidOperationException("Runtime provider configuration is required.");
        var policy = configuration.GetSection(AgentDefinitionPolicyConfig.ConfigurationSectionName).Get<AgentDefinitionPolicyConfig>()
            ?? throw new InvalidOperationException("Runtime provider policy is required.");
        var candidates = metadata.Deployments
            .Where(item => item.Model == metadata.ApprovedModel && item.Revision == metadata.ApprovedRevision)
            .SelectMany(item => item.Endpoints.Where(endpoint =>
                policy.AllowedProviderAuthorities.Contains(new Uri(endpoint).GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
                .Select(endpoint => (Deployment: item, Endpoint: new Uri(endpoint))))
            .DistinctBy(item => (item.Deployment.DeploymentResourceId, item.Endpoint)).ToArray();
        Assert.Single(candidates.Select(item => item.Deployment.Model));
        var deployment = candidates[0].Deployment;
        var provider = new ProviderConfig
        {
            Type = AgentType.AzureOpenAI,
            Endpoint = candidates[0].Endpoint,
            ModelName = deployment.DeploymentName,
            UseResponsesApi = true,
            ReasoningEffort = Microsoft.Extensions.AI.ReasoningEffort.Low,
        };
        Assert.NotNull(provider.Endpoint);
        output.WriteLine($"Pinned public model: {deployment.Model}, revision {deployment.Revision}.");
        var configuredSources = configuration.GetSection(DocumentIngestionConfig.ConfigurationSectionName).Get<DocumentIngestionConfig>()
            ?? throw new InvalidOperationException("Existing trusted source bindings are required.");
        var sources = configuredSources.Sources.Where(source => selections.ContainsKey(source.TenantId)).ToArray();
        Assert.Equal(2, sources.Length);
        var ingestion = configuredSources with { ProcessingEnabled = true, ProcessingTimeoutSeconds = 900, Sources = sources.ToList() };
        var runtime = configuredRuntime with
        {
            Tenants = sources.ToDictionary(source => source.TenantId, _ => new TenantAgentConfig
            {
                Providers = new() { [ProviderName] = provider },
                EntraIdProviders = new(StringComparer.Ordinal) { ProviderName },
            }),
        };
        Assert.Contains(provider.Endpoint.GetLeftPart(UriPartial.Authority), policy.AllowedProviderAuthorities, StringComparer.OrdinalIgnoreCase);
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IDbContextFactory<AgentRuntimeDbContext>>(factory)
            .AddSingleton(Options.Create(runtime))
            .AddSingleton(Options.Create(policy))
            .AddSingleton(Options.Create(new ApiAuthConfig { Username = "unused", Password = "unused" }))
            .AddSingleton(credential);
        foreach (var name in new[] { nameof(DocumentPreparationProvider), nameof(DocumentPreparationModelVerifier) })
            services.AddHttpClient(name).RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        await using var container = services.BuildServiceProvider();
        var clients = container.GetRequiredService<IHttpClientFactory>();
        var preparationProvider = new DocumentPreparationProvider(container, clients, credential, new(clients, credential));
        var limits = new DocumentPreparationConfig();
        var preparation = new PdfDocumentPreparationService(Options.Create(limits), new DocumentChunkingService());
        var downloader = new DocumentBlobDownloadService(Options.Create(ingestion), credential);
        var inputs = new List<SourceInput>();
        foreach (var source in sources)
        {
            var settings = new DocumentAgentPreparationConfig
            {
                AgentName = DocumentPreparationAgentTemplate.AgentName,
                DefinitionVersion = DocumentPreparationAgentTemplate.Version,
                Model = deployment.Model,
                ModelRevision = deployment.Revision,
                DeploymentResourceId = deployment.DeploymentResourceId,
                ManagementEndpoint = "https://management.azure.com/",
                DocumentIntelligenceEndpoint = metadata.LayoutEndpoint,
                MaximumPages = PageBudget,
                PhysicalPages = selections[source.TenantId],
                MaximumOutputTokens = DocumentPreparationAgentTemplate.MaximumOutputTokens,
                RequestTimeoutSeconds = 120,
            };
            var definition = new AgentDefinition
            {
                Name = settings.AgentName,
                Version = settings.DefinitionVersion,
                Agent = Assert.IsType<AgentConfig>(DocumentPreparationAgentTemplate.Create().Definition.GetProperty("agent")
                    .Deserialize<AgentConfig>(JsonSerializerOptions.Web)) with
                {
                    Provider = ProviderName,
                },
                Provider = provider,
            };
            var plan = new DocumentPreparationPlan
            {
                TenantId = source.TenantId,
                Definition = definition,
                DefinitionSha256 = DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(definition, JsonSerializerOptions.Web)),
                Settings = settings,
            };
            await preparationProvider.VerifyModelAsync(plan, Token);
            var blobs = new BlobContainerClient(new Uri(source.ContainerUri), credential.Credential);
            var names = new List<string>();
            await foreach (var blob in blobs.GetBlobsAsync(cancellationToken: Token))
                if (blob.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(blob.Name);
                    if (names.Count == 2)
                        break;
                }
            if (names.Count != 1)
                throw new InvalidOperationException("Acceptance requires exactly one existing PDF per approved source.");
            var client = blobs.GetBlobClient(names[0]);
            var properties = (await client.GetPropertiesAsync(cancellationToken: Token)).Value;
            Assert.InRange(properties.ContentLength, 5, limits.MaximumPdfBytes);
            var preflight = new DocumentIngestionClaim
            {
                JobId = "preflight",
                DocumentId = "preflight",
                ClaimId = Guid.Empty,
                TenantId = source.TenantId,
                CollectionId = source.CollectionId,
                Sequencer = "0",
                BlobUri = client.Uri.AbsoluteUri,
                ETag = properties.ETag.ToString(),
                ExpiresAtUtc = DateTimeOffset.MaxValue,
            };
            using var bytes = new MemoryStream();
            await using (var stream = await downloader.OpenReadAsync(preflight, Token))
                await preparation.ReadBoundedAsync(stream, bytes, Token);
            Assert.Equal(properties.ContentLength, bytes.Length);
            var original = bytes.ToArray();
            using var pdf = PdfDocument.Open(original, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false });
            Assert.False(pdf.IsEncrypted);
            Assert.InRange(pdf.NumberOfPages, 1, limits.MaximumPages);
            Assert.All(settings.PhysicalPages, page => Assert.InRange(page, 1, pdf.NumberOfPages));
            inputs.Add(new(source, plan, client, properties.ETag.ToString(), properties.LastModified,
                original, pdf.NumberOfPages));
        }
        Directory.CreateDirectory(directory);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync(Token);
            foreach (var input in inputs)
                db.ActiveAgentDefinitions.Add(new()
                {
                    TenantId = input.Source.TenantId,
                    AgentName = input.Plan.Definition.Name,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Snapshot = new AgentDefinitionSnapshotEntity
                    {
                        TenantId = input.Source.TenantId,
                        AgentName = input.Plan.Definition.Name,
                        DefinitionVersion = input.Plan.Definition.Version,
                        SchemaVersion = 1,
                        DefinitionJson = JsonSerializer.Serialize(input.Plan.Definition, JsonSerializerOptions.Web),
                        PublishedAtUtc = DateTimeOffset.UtcNow,
                    },
                });
            await db.SaveChangesAsync(Token);
        }
        var inbox = new DocumentIngestionInboxService(TimeProvider.System, factory);
        var jobs = new DocumentIngestionJobService(Options.Create(ingestion), TimeProvider.System, factory);
        var ledger = new DocumentPreparationOperationStore(TimeProvider.System, factory);
        var worker = new HybridDocumentProcessor(NullLogger<HybridDocumentProcessor>.Instance, Options.Create(limits),
            TimeProvider.System, downloader, preparation, ledger, preparationProvider);
        var review = new StringBuilder("<!doctype html><meta charset='utf-8'><title>Production preparation review</title>"
            + "<style>body{font-family:system-ui;margin:2em}section{display:grid;grid-template-columns:repeat(3,1fr);gap:1em}"
            + "pre{white-space:pre-wrap;overflow-wrap:anywhere}iframe{width:100%;height:75vh}article{border-top:1px solid #888}</style>"
            + "<h1>Production preparation review</h1><p>Review-only candidates. Review annotations are not document content. Usage is retained in separate exports; it is not an invoice or semantic acceptance.</p>");
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var notification = BlobStorageEventReader.Read(BinaryData.FromObjectAsJson(new
            {
                id = $"acceptance-{Guid.NewGuid():N}",
                topic = input.Source.Topic,
                subject = $"/blobServices/default/containers/{input.Blob.BlobContainerName}/blobs/{input.Blob.Name}",
                eventType = "Microsoft.Storage.BlobCreated",
                eventTime = DateTimeOffset.UtcNow,
                dataVersion = "1",
                metadataVersion = "1",
                data = new { url = input.Blob.Uri.AbsoluteUri, sequencer = "0", eTag = input.ETag },
            }));
            await inbox.AcceptAsync(new()
            {
                Event = notification,
                TenantId = input.Source.TenantId,
                CollectionId = input.Source.CollectionId,
                AgentPreparation = input.Plan.Settings,
            }, Token);
            var claim = Assert.IsType<DocumentIngestionClaim>(await jobs.ClaimAsync(Token));
            Assert.Equal(input.Source.TenantId, claim.TenantId);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(900));
            var outcome = await worker.ProcessAsync(claim, deadline.Token);
            Assert.True(await jobs.FinishAsync(claim, outcome, Token));
            Assert.Equal(DocumentProcessingOutcome.ReviewRequired, outcome);
            var operations = await ledger.ReadAsync(claim, Token);
            Assert.Equal(PageBudget + 1, operations.Count);
            Assert.All(operations, operation =>
            {
                Assert.Equal(DocumentPreparationOperations.Validated, operation.Status);
                Assert.Equal(DocumentPreparationIdentity.Hash(input.Bytes), operation.SourceSha256);
            });
            var after = (await input.Blob.GetPropertiesAsync(cancellationToken: Token)).Value;
            Assert.Equal(input.ETag, after.ETag.ToString());
            Assert.Equal(input.ModifiedAtUtc, after.LastModified);
            Assert.Equal(input.Bytes.LongLength, after.ContentLength);
            var prefix = $"source-{index + 1}";
            await File.WriteAllBytesAsync(Path.Combine(directory, prefix + ".pdf"), input.Bytes, Token);
            var managed = operations.Single(operation => operation.Stage == DocumentPreparationOperations.DocumentIntelligence);
            var layout = Assert.IsType<DocumentLayoutPollResult>(JsonSerializer.Deserialize<DocumentLayoutPollResult>(managed.ResultJson!, JsonSerializerOptions.Web));
            var evidence = operations.Select(operation => new { operation.Stage, operation.PageNumber, operation.Status, operation.UsageJson });
            await File.WriteAllTextAsync(Path.Combine(directory, prefix + ".usage.json"), JsonSerializer.Serialize(new
            {
                originalSha256 = DocumentPreparationIdentity.Hash(input.Bytes),
                originalPageCount = input.PageCount,
                physicalPages = input.Plan.Settings.PhysicalPages,
                model = deployment.Model,
                revision = deployment.Revision,
                operations = evidence,
            }, EvidenceJson), Token);
            foreach (var page in input.Plan.Settings.PhysicalPages.Order())
            {
                var operation = operations.Single(operation => operation.Stage == DocumentPreparationOperations.PreparationAgent && operation.PageNumber == page);
                var receipt = Assert.IsType<DocumentPreparationAgentReceipt>(JsonSerializer.Deserialize<DocumentPreparationAgentReceipt>(
                    operation.ResultJson!, JsonSerializerOptions.Web));
                var candidate = DocumentPreparationCandidateValidator.Validate(receipt.Response, input.Plan, page, limits.MaximumExtractedCharacters);
                review.Append($"<article><h2>Source {index + 1}, original page {page}</h2><section>"
                    + $"<div><h3>Original</h3><iframe src='{prefix}.pdf#page={page}'></iframe></div>"
                    + $"<div><h3>Managed draft</h3><pre>{WebUtility.HtmlEncode(layout.Pages[page])}</pre></div>"
                    + $"<div><h3>Preparation agent</h3>{DocumentPreparationReviewRenderer.Render(candidate)}</div></section></article>");
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), review.ToString(), Token);
            output.WriteLine($"Source ordinal {index + 1}: {PageBudget} original pages, review-only receipts retained.");
        }
        await using var verification = factory.CreateDbContext();
        Assert.Equal(2, await verification.DocumentIngestionJobs.CountAsync(job => job.Status == DocumentIngestionStates.ReviewRequired, Token));
        Assert.Equal(8, await verification.DocumentPreparationOperations.CountAsync(Token));
        Assert.Empty(await verification.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Empty(await verification.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await verification.AgentRuns.ToArrayAsync(Token));
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Explicit {name} is required.");

    private sealed record AzureMetadata(DeploymentMetadata[] Deployments, string LayoutEndpoint, string ApprovedModel, string ApprovedRevision);
    private sealed record DeploymentMetadata(string DeploymentName, string DeploymentResourceId, string Model, string Revision, string[] Endpoints);
    private sealed record SourceInput(DocumentSourceConfig Source, DocumentPreparationPlan Plan, BlobClient Blob,
        string ETag, DateTimeOffset ModifiedAtUtc, byte[] Bytes, int PageCount);
}
#endif
