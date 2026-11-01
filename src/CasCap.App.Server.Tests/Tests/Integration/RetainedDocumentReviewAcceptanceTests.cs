#if DEBUG
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using CasCap.Entities;
using CasCap.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Opt-in HTTP acceptance over retained real-document receipts; never downloads or invokes preparation.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "RetainedDocumentReview")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RetainedDocumentReviewAcceptanceTests(ITestOutputHelper output)
{
    private const string ReviewMigration = "20261028120307_DocumentMarkdownReviewGenerations";
    private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Gets the separate retained-ledger mutation gate; ordinary runs cannot access operator documents.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("CASCAP_RUN_RETAINED_DOCUMENT_REVIEW") == "true";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires explicit approval of retained local ledger migration/storage and private reviewed artifacts.")]
    public async Task RetainedReceiptsToProtectedGenerations()
    {
        var original = RequiredDirectory("CASCAP_DOCUMENT_REVIEW_INPUT");
        var reviewed = RequiredDirectory("CASCAP_DOCUMENT_REVIEW_EXPECTED");
        var annotations = RequiredDirectory("CASCAP_DOCUMENT_REVIEW_ANNOTATIONS");
        var directory = Required("CASCAP_DOCUMENT_REVIEW_OUTPUT");
        Assert.True(Path.IsPathFullyQualified(directory));
        Assert.False(Directory.Exists(directory), "Use a fresh private output directory.");
        var connection = new NpgsqlConnectionStringBuilder(Required("ConnectionStrings__PostgreSql"));
        Assert.True(connection.Host == "127.0.0.1" && connection.Port == 5432
            && connection.Database is { } name && name.StartsWith("agentizr_acceptance_", StringComparison.Ordinal),
            "Only an explicitly selected retained local acceptance ledger is permitted.");
        var factory = new PooledDbContextFactory<AgentRuntimeDbContext>(
            new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await using var db = factory.CreateDbContext();
        Assert.True(await db.Database.CanConnectAsync(Token), "The existing paid ledger must be retained, never recreated.");
        var applied = (await db.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        Assert.Contains("20261027125842_DocumentHybridPreparationOperations", applied);
        var pending = (await db.Database.GetPendingMigrationsAsync(Token)).ToArray();
        Assert.True(pending.Length == 0 || pending.SequenceEqual([ReviewMigration]),
            "Only the reviewed additive Markdown-generation migration is authorized.");
        var jobs = await db.DocumentIngestionJobs.AsNoTracking().OrderBy(item => item.Id).Take(3).ToArrayAsync(Token);
        Assert.Equal(2, jobs.Length);
        Assert.Equal(2, jobs.Select(job => job.TenantId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(jobs, job =>
        {
            Assert.Equal(DocumentIngestionStates.ReviewRequired, job.Status);
            Assert.Null(job.ClaimId);
            Assert.NotNull(job.FinishedAtUtc);
        });
        var receipts = await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id).Take(9).ToArrayAsync(Token);
        Assert.Equal(8, receipts.Length);
        Assert.All(receipts, receipt => Assert.Equal(DocumentPreparationOperations.Validated, receipt.Status));
        var receiptDigest = Digest(receipts);
        var jobDigest = Digest(jobs);
        var documents = await db.KnowledgeDocuments.AsNoTracking().OrderBy(item => item.Id).Take(3).ToArrayAsync(Token);
        Assert.Equal(2, documents.Length);
        var documentDigest = Digest(documents);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new List<SourceInput>();
        for (var ordinal = 1; ordinal <= 2; ordinal++)
        {
            var pdf = await ReadAsync(Path.Combine(original, $"source-{ordinal}.pdf"), 32 * 1024 * 1024);
            var pages = await JsonAsync<DocumentMarkdownPage[]>(Path.Combine(reviewed, $"source-{ordinal}.pages.json"));
            var projection = await JsonAsync<DocumentMarkdownChunkingResult>(Path.Combine(reviewed, $"source-{ordinal}.chunks.json"));
            var review = await JsonAsync<DocumentMarkdownSourceReview>(Path.Combine(annotations, $"source-{ordinal}.review.json"));
            var sourceHash = DocumentPreparationIdentity.Hash(pdf);
            var sourceJobIds = receipts.Where(receipt => receipt.SourceSha256 == sourceHash).Select(receipt => receipt.JobId).Distinct().ToArray();
            Assert.Single(sourceJobIds);
            var job = Assert.Single(jobs, job => job.Id == sourceJobIds[0]);
            Assert.Equal(3, pages.Length);
            int[] expectedPages = ordinal == 1 ? [2, 3, 6] : [6, 7, 12];
            Assert.Equal(expectedPages, pages.Select(page => page.PageNumber).ToArray());
            Assert.Equal(Digest(projection), Digest(new DocumentChunkingService().ChunkMarkdown(pages, new(), new(), review, Token)));
            Assert.Equal(ordinal == 1 ? 23 : 10, projection.Chunks.Count);
            Assert.All(projection.Chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 512));
            Assert.DoesNotContain(projection.Chunks, chunk => chunk.IsFragmented);
            sources.Add(new(ordinal, job, sourceHash, pages, projection, review));
        }
        Assert.Equal(2, sources.Select(source => source.Job.Id).Distinct().Count());
        await db.Database.MigrateAsync(Token);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Token));
        var before = await db.DocumentMarkdownGenerations.AsNoTracking().OrderBy(item => item.Id).Take(3).ToArrayAsync(Token);
        Assert.True(before.Length is 0 or 2, "Only zero generations or the exact retained retry pair is supported.");
        if (before.Length != 0)
            foreach (var source in sources)
            {
                var existing = Assert.Single(before, item => item.JobId == source.Job.Id
                    && item.TenantId == source.Job.TenantId && item.CollectionId == source.Job.CollectionId
                    && item.DocumentId == source.Job.DocumentId && item.Sequencer == source.Job.Sequencer);
                var snapshot = Assert.IsType<DocumentMarkdownReviewGeneration>(
                    JsonSerializer.Deserialize<DocumentMarkdownReviewGeneration>(existing.PayloadJson, EvidenceJson));
                Assert.Equal(source.SourceSha256, snapshot.SourceSha256);
                Assert.Equal(source.Projection.Id, existing.ProjectionId);
                Assert.Equal(Digest(source.Projection), Digest(snapshot.Projection));
            }
        await using var host = new DocumentReviewWebApplicationFactory(factory, jobs.Select(job => job.TenantId).ToArray());
        using var http = host.CreateClient();
        await AdministrationTestClient.PrepareAsync(http, AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        foreach (var source in sources)
        {
            var job = source.Job;
            var preview = await client.GetDocumentReviewAsync(job.TenantId, job.CollectionId, job.DocumentId, job.Id, null, Token);
            Assert.True(preview.IsSuccess, "Retained-source HTTP preview failed.");
            Assert.Null(preview.Value!.GenerationId);
            Assert.True(source.SourceSha256 == preview.Value.SourceSha256, "The original PDF digest must match.");
            Assert.Equal(Digest(source.Pages), Digest(preview.Value.Pages.Select(page => new DocumentMarkdownPage(page.Page, page.Markdown)).ToArray()));
        }
        Assert.Equal(before.Length, await db.DocumentMarkdownGenerations.CountAsync(Token));
        Directory.CreateDirectory(directory);
        var reports = new List<object>();
        foreach (var source in sources)
        {
            var job = source.Job;
            var document = Assert.Single(documents, item => item.Id == job.DocumentId);
            var request = new DocumentReviewGenerationRequest
            {
                Sequencer = job.Sequencer,
                ETag = document.ETag!,
                MaximumTokensPerChunk = 512,
                OverlapTokens = 64,
                Review = JsonSerializer.SerializeToElement(source.Review, JsonSerializerOptions.Web),
            };
            var stored = await client.StoreDocumentReviewAsync(job.TenantId, job.CollectionId, job.DocumentId, job.Id, request, Token);
            Assert.True(stored.IsSuccess, "Retained-source HTTP storage failed.");
            Assert.NotNull(stored.Value!.GenerationId);
            var retry = await client.StoreDocumentReviewAsync(job.TenantId, job.CollectionId, job.DocumentId, job.Id, request, Token);
            Assert.True(retry.IsSuccess, "Retained-source HTTP retry failed.");
            Assert.Equal(stored.Value.GenerationId, retry.Value!.GenerationId);
            var read = await client.GetDocumentReviewAsync(job.TenantId, job.CollectionId, job.DocumentId, job.Id, stored.Value.GenerationId, Token);
            Assert.True(read.IsSuccess, "Retained-generation HTTP read failed.");
            Assert.Equal(DocumentIngestionStates.ReviewRequired, read.Value!.State);
            Assert.Equal(source.SourceSha256, read.Value.SourceSha256);
            Assert.Equal(Digest(source.Projection), Digest(read.Value.Projection.Deserialize<DocumentMarkdownChunkingResult>(EvidenceJson)));
            Assert.Equal(Digest(stored.Value), Digest(read.Value));
            await File.WriteAllTextAsync(Path.Combine(directory, $"source-{source.Ordinal}.generation.json"),
                JsonSerializer.Serialize(read.Value, EvidenceJson), Token);
            reports.Add(new
            {
                source.Ordinal,
                read.Value.GenerationId,
                ChunkCount = source.Projection.Chunks.Count,
                MaximumTokens = source.Projection.Chunks.Max(chunk => chunk.TokenCount),
                State = DocumentIngestionStates.ReviewRequired,
                ProjectionSha256 = Digest(source.Projection),
            });
            output.WriteLine($"Source ordinal {source.Ordinal}: {source.Projection.Chunks.Count} reviewed chunks stored and reread; retry reused identity.");
        }
        var after = await db.DocumentMarkdownGenerations.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token);
        Assert.Equal(2, after.Length);
        foreach (var prior in before)
            Assert.Equal(Digest(prior), Digest(Assert.Single(after, item => item.Id == prior.Id)));
        Assert.Equal(receiptDigest, Digest(await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token)));
        Assert.Equal(jobDigest, Digest(await db.DocumentIngestionJobs.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token)));
        Assert.Equal(documentDigest, Digest(await db.KnowledgeDocuments.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token)));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Empty(await db.AgentRuns.ToArrayAsync(Token));
        foreach (var (path, digest) in inputs)
            Assert.Equal(digest, DocumentPreparationIdentity.Hash(await File.ReadAllBytesAsync(path, Token)));
        await File.WriteAllTextAsync(Path.Combine(directory, "validation.json"), JsonSerializer.Serialize(new
        {
            Scope = "retained-local-ledger-test-authenticated-http",
            AppliedMigrations = pending,
            Sources = reports,
            OriginalArtifactsUnchanged = true,
            ReceiptsUnchanged = true,
            JobsAndDocumentsUnchanged = true,
            ProviderCalls = 0,
            EmbeddedChunks = 0,
            ApprovedGenerations = 0,
        }, EvidenceJson), Token);

        async Task<byte[]> ReadAsync(string path, long maximum)
        {
            Assert.InRange(new FileInfo(path).Length, 1, maximum);
            var bytes = await File.ReadAllBytesAsync(path, Token);
            Assert.InRange(bytes.LongLength, 1, maximum);
            inputs.Add(path, DocumentPreparationIdentity.Hash(bytes));
            return bytes;
        }

        async Task<T> JsonAsync<T>(string path) where T : class =>
            Assert.IsType<T>(JsonSerializer.Deserialize<T>(await ReadAsync(path, 1024 * 1024), EvidenceJson));
    }

    private static string Digest<T>(T value) => DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(value, JsonSerializerOptions.Web));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Explicit {name} is required.");

    private static string RequiredDirectory(string name)
    {
        var path = Required(name);
        Assert.True(Path.IsPathFullyQualified(path) && Directory.Exists(path), "An existing private input directory is required.");
        return path;
    }

    private sealed record SourceInput(int Ordinal, DocumentIngestionJobEntity Job, string SourceSha256,
        DocumentMarkdownPage[] Pages, DocumentMarkdownChunkingResult Projection, DocumentMarkdownSourceReview Review);
}
#endif
