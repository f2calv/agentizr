using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Fact]
    public async Task Knowledge_PublicReferenceInheritsCollectionAccessAndWithdrawalsChangeCorpusIdentity()
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync(access: DocumentCollectionAccess.PublicReference);
        var generation = Assert.Single(observed);
        var ordinal = generation.Manifest.Inputs[0].Chunk.Ordinal;
        var initialCorpus = await store.PublicCorpusIdAsync(binding, Token);
        Assert.NotNull(await store.ReadPassageAsync(binding, generation.JobId, ordinal, Token));
        Assert.Single(await store.SearchAsync(binding, observed, [1, 2, 3, 4, 5, 6, 7, 8], 1, Token));

        await using (var db = await _database.DbContextFactory.CreateDbContextAsync(Token))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Token);
            await DocumentIngestionTransactions.LockAsync(db, Token);
            (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
            await db.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        Assert.NotEqual(initialCorpus, await store.PublicCorpusIdAsync(binding, Token));
        Assert.Empty(await store.SearchAsync(binding, observed, [1, 2, 3, 4, 5, 6, 7, 8], 3, Token));
        Assert.Null(await store.ReadPassageAsync(binding, generation.JobId, ordinal, Token));
    }

    [Fact]
    public async Task Knowledge_ExactRankingFiltersBeforeTopKAndResolvesBoundedCitations()
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync();
        var generation = Assert.Single(observed);
        Assert.True(generation.Manifest.Inputs.Count > 1);
        var target = generation.Manifest.Inputs[^1].Chunk.Ordinal;
        await using var db = _database.DbContextFactory.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE agent_runtime.document_index_vectors
            SET embedding = CASE WHEN ordinal = {target} THEN '[1,0,0,0,0,0,0,0]'::vector
                ELSE '[0,1,0,0,0,0,0,0]'::vector END WHERE job_id = {generation.JobId}
            """, Token);
        var hits = await store.SearchAsync(binding, observed, [1, 0, 0, 0, 0, 0, 0, 0], 2, Token);
        Assert.Equal(2, hits.Count);
        Assert.Equal(target, hits[0].Passage.ChunkOrdinal);
        Assert.InRange(hits[0].Similarity, 0.99999, 1);
        Assert.InRange(hits[1].Similarity, -0.00001, 0.00001);
        Assert.All(hits, hit =>
        {
            Assert.InRange(hit.Passage.Text.Length, 1, 8_000);
            Assert.InRange(hit.Passage.SourceSpans.Count, 1, 64);
            Assert.All(hit.Passage.SourceSpans, span => Assert.True(span.PageNumber > 0 && span.EndOffset > span.StartOffset));
            Assert.Equal(generation.Manifest.SourceSha256, hit.Passage.SourceSha256);
            Assert.Contains(generation.Manifest.Inputs, input => input.Chunk.Ordinal == hit.Passage.ChunkOrdinal);
        });
        var passage = await store.ReadPassageAsync(binding, generation.JobId, target, Token);
        Assert.Equal(hits[0].Passage.Text, passage!.Text);
        Assert.Empty(await store.ReadAsync(binding with { TenantId = "tenant-b" }, Token));
        Assert.Empty(await store.ReadAsync(binding with { CollectionId = "another-collection" }, Token));
        Assert.Null(await store.ReadPassageAsync(binding with { TenantId = "tenant-b" }, generation.JobId, target, Token));
    }

    [Fact]
    public async Task Knowledge_ExcludedPassagesNeverReturnSearchHitsOrPreviews()
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync(excludeFirst: true);
        var generation = Assert.Single(observed);
        Assert.DoesNotContain(generation.Manifest.Inputs, input => input.Chunk.Ordinal == 0);
        var hits = await store.SearchAsync(binding, observed, [1, 2, 3, 4, 5, 6, 7, 8], 8, Token);
        Assert.NotEmpty(hits);
        Assert.DoesNotContain(hits, hit => hit.Passage.ChunkOrdinal == 0);
        Assert.Null(await store.ReadPassageAsync(binding, generation.JobId, 0, Token));
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("etag")]
    [InlineData("selection")]
    [InlineData("assessment")]
    [InlineData("policy")]
    [InlineData("profile")]
    [InlineData("pointer")]
    public async Task Knowledge_WithdrawnOrIncompatibleGenerationsNeverReturnPassages(string change)
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync();
        var generation = Assert.Single(observed);
        await using var db = _database.DbContextFactory.CreateDbContext();
        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            await DocumentIngestionTransactions.LockAsync(db, Token);
            switch (change)
            {
                case "deleted":
                    (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
                    break;
                case "etag":
                    (await db.KnowledgeDocuments.SingleAsync(Token)).ETag = "\"changed\"";
                    break;
                case "selection":
                    var selection = await db.DocumentChunkReviews.SingleAsync(Token);
                    db.DocumentChunkReviews.Add(new()
                    {
                        TenantId = selection.TenantId,
                        GenerationId = selection.GenerationId,
                        Revision = 2,
                        ActorId = "synthetic-reviewer",
                        ExclusionsJson = selection.ExclusionsJson,
                        RecordedAtUtc = DateTimeOffset.UtcNow,
                    });
                    break;
                case "assessment":
                    var approval = await db.DocumentApprovals.SingleAsync(Token);
                    db.DocumentApprovals.Add(new()
                    {
                        TenantId = approval.TenantId,
                        GenerationId = approval.GenerationId,
                        Revision = 2,
                        ChunkReviewRevision = approval.ChunkReviewRevision,
                        ActorId = "synthetic-reviewer",
                        AssessmentJson = approval.AssessmentJson,
                        RecordedAtUtc = DateTimeOffset.UtcNow,
                    });
                    break;
                case "pointer":
                    await db.ActiveDocumentIndexes.ExecuteDeleteAsync(Token);
                    break;
            }
            await db.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        if (change is "policy" or "profile")
        {
            var settings = DocumentIndexingTestData.ExecutionConfig(_source.TenantId, _source.CollectionId);
            settings = change == "policy"
                ? settings with { Collections = [settings.Collections[0] with { Revision = 2 }] }
                : settings with { Profiles = [settings.Profiles[0] with { ModelRevision = "different" }] };
            store = new(Options.Create(settings), _database.DbContextFactory, IndexStore(settings));
        }
        Assert.Empty(await store.SearchAsync(binding, observed, [1, 2, 3, 4, 5, 6, 7, 8], 3, Token));
        Assert.Null(await store.ReadPassageAsync(binding, generation.JobId, generation.Manifest.Inputs[0].Chunk.Ordinal, Token));
    }

    [Fact]
    public async Task Knowledge_SearchWaitsForMutationFenceAndRechecksSourceAfterAcquisition()
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync();
        await using var db = _database.DbContextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(db, Token);
        var pending = store.SearchAsync(binding, observed, [1, 2, 3, 4, 5, 6, 7, 8], 3, Token);
        await _database.WaitForBlockedAdvisoryLockAsync(Token);
        (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
        await db.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Empty(await pending);
    }

    [Theory]
    [InlineData("A1", true)]
    [InlineData("A2", false)]
    public async Task Knowledge_ReviewedDocumentLevelIsFilteredBeforeRanking(string reviewedLevel, bool permitted)
    {
        var (store, binding, observed) = await PublishedKnowledgeAsync(reviewedLevel);
        var restricted = binding with { MaximumCefrLevel = "A1" };
        var hits = await store.SearchAsync(restricted, observed, [1, 2, 3, 4, 5, 6, 7, 8], 1, Token);
        Assert.Equal(permitted ? 1 : 0, hits.Count);
        var generation = Assert.Single(observed);
        var passage = await store.ReadPassageAsync(restricted, generation.JobId, generation.Manifest.Inputs[0].Chunk.Ordinal, Token);
        Assert.Equal(permitted, passage is not null);
    }

    private async Task<(DocumentKnowledgeStore Store, DocumentKnowledgeBindingConfig Binding,
        IReadOnlyList<DocumentKnowledgeGeneration> Generations)> PublishedKnowledgeAsync(
        string reviewedLevel = "A1", bool excludeFirst = false,
        DocumentCollectionAccess access = DocumentCollectionAccess.Private)
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync(
            excludeFirst: excludeFirst, cefrLevel: reviewedLevel, access: access);
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var indexing = IndexStore(settings);
            var provider = new EmbeddingProvider();
            while (await Processor(indexing, provider, settings).ProcessOneAsync(Token))
            {
                Assert.True(provider.Submissions < 100);
                await ExpireEmbeddingPacingAsync();
            }
            var store = new DocumentKnowledgeStore(Options.Create(settings), _database.DbContextFactory, indexing);
            var binding = new DocumentKnowledgeBindingConfig
            {
                TenantId = _source.TenantId,
                AgentName = "assistant",
                CollectionId = _source.CollectionId,
                MaximumCefrLevel = reviewedLevel,
            };
            var generations = await store.ReadAsync(binding, Token);
            Assert.Single(generations);
            return (store, binding, generations);
        }
    }
}
