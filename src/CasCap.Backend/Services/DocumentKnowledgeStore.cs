using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Searches only tenant-qualified, current reviewed publications using the existing mutation fence.</summary>
internal sealed class DocumentKnowledgeStore(
    IOptions<DocumentIndexingConfig> config,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    DocumentIndexingStore indexingStore)
{
    /// <summary>Captures eligible generations before external inference, without holding database locks over HTTP.</summary>
    public async Task<IReadOnlyList<DocumentKnowledgeGeneration>> ReadAsync(
        DocumentKnowledgeBindingConfig binding, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        return await CurrentAsync(db, binding, token);
    }

    /// <summary>Revalidates and ranks current generations in one fenced transaction after inference.</summary>
    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        DocumentKnowledgeBindingConfig binding,
        IReadOnlyList<DocumentKnowledgeGeneration> observed,
        float[] vector,
        int maximumResults,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var current = (await CurrentAsync(db, binding, token))
            .Where(item => observed.Any(previous => previous.JobId == item.JobId
                && JsonSerializer.Serialize(previous.Manifest, JsonSerializerOptions.Web)
                    == JsonSerializer.Serialize(item.Manifest, JsonSerializerOptions.Web))).ToArray();
        var inputs = current.SelectMany(item => item.Manifest.Inputs.Where(input => CanReturn(input.Chunk))
            .Select(input => new { job_id = item.JobId, ordinal = input.Chunk.Ordinal, input_sha256 = input.InputSha256 })).ToArray();
        if (inputs.Length == 0)
            return [];
        var payload = JsonSerializer.Serialize(inputs);
        var query = JsonSerializer.Serialize(vector);
        var rows = await db.Database.SqlQuery<DocumentKnowledgeMatch>($"""
            SELECT vectors.job_id AS "JobId", vectors.ordinal AS "Ordinal",
                vectors.embedding <=> CAST({query} AS vector) AS "Distance"
            FROM agent_runtime.document_index_vectors vectors
            JOIN jsonb_to_recordset(CAST({payload} AS jsonb))
                AS allowed(job_id text, ordinal integer, input_sha256 text)
                ON vectors.job_id = allowed.job_id AND vectors.ordinal = allowed.ordinal
                    AND vectors.input_sha256 = allowed.input_sha256
            WHERE vectors.tenant_id = {binding.TenantId}
            ORDER BY "Distance", vectors.job_id COLLATE "C", vectors.ordinal
            LIMIT {maximumResults}
            """).ToArrayAsync(token);
        return rows.Select(row =>
        {
            if (!double.IsFinite(row.Distance))
                throw new InvalidDataException("A published vector produced an invalid cosine distance.");
            var generation = current.Single(item => item.JobId == row.JobId);
            return new KnowledgeSearchHit
            {
                Passage = Passage(generation, generation.Manifest.Inputs.Single(input => input.Chunk.Ordinal == row.Ordinal).Chunk),
                Similarity = Math.Clamp(1 - row.Distance, -1, 1),
            };
        }).ToArray();
    }

    /// <summary>Resolves a citation only while its source, selection, assessment and active pointer remain eligible.</summary>
    public async Task<KnowledgePassageResponse?> ReadPassageAsync(
        DocumentKnowledgeBindingConfig binding, string indexJobId, int ordinal, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var generation = (await CurrentAsync(db, binding, token)).SingleOrDefault(item => item.JobId == indexJobId);
        var chunk = generation?.Manifest.Inputs.SingleOrDefault(input => input.Chunk.Ordinal == ordinal)?.Chunk;
        return generation is not null && chunk is not null && CanReturn(chunk) ? Passage(generation, chunk) : null;
    }

    /// <summary>Identifies the current public-reference corpus without reading vectors or sending content externally.</summary>
    public async Task<string> PublicCorpusIdAsync(DocumentKnowledgeBindingConfig binding, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var current = await CurrentAsync(db, binding, token, validateVectors: false);
        return DocumentPreparationIdentity.Digest(current.Select(item => item.JobId).ToArray());
    }

    private async Task<IReadOnlyList<DocumentKnowledgeGeneration>> CurrentAsync(
        AgentRuntimeDbContext db, DocumentKnowledgeBindingConfig binding, CancellationToken token, bool validateVectors = true)
    {
        if (!db.Database.IsNpgsql())
            throw new InvalidOperationException("Knowledge retrieval requires native PostgreSQL vector storage.");
        var settings = config.Value;
        var policy = settings.Collections.SingleOrDefault(item => item.TenantId == binding.TenantId
            && item.CollectionId == binding.CollectionId);
        if (!settings.Enabled || policy is null)
            return [];
        var jobs = await db.DocumentIndexingJobs.AsNoTracking()
            .Where(job => job.TenantId == binding.TenantId && job.CollectionId == binding.CollectionId
                && job.Status == DocumentIndexingStates.Published
                && db.ActiveDocumentIndexes.Any(active => active.TenantId == job.TenantId
                    && active.CollectionId == job.CollectionId && active.DocumentId == job.DocumentId && active.JobId == job.Id))
            .OrderBy(job => job.Id).Take(33).ToArrayAsync(token);
        if (jobs.Length > 32)
            throw new InvalidOperationException("Exact knowledge retrieval supports at most 32 active documents per collection.");
        var result = new List<DocumentKnowledgeGeneration>();
        foreach (var job in jobs)
        {
            var manifest = await indexingStore.CurrentManifestAsync(db, job, token);
            if (manifest is null)
                continue;
            var assessmentJson = await db.DocumentApprovals.AsNoTracking()
                .Where(item => item.TenantId == binding.TenantId && item.GenerationId == manifest.GenerationId
                    && item.Revision == manifest.AssessmentRevision)
                .Select(item => item.AssessmentJson).SingleAsync(token);
            var assessment = JsonSerializer.Deserialize<DocumentApprovalRequest>(assessmentJson, JsonSerializerOptions.Web)
                ?? throw new InvalidDataException("The saved document assessment is empty.");
            if (assessment.CefrLevel is not { } level || !AllowsLevel(binding.MaximumCefrLevel, level))
                continue;
            if (validateVectors)
                await DocumentVectorStore.ValidateAsync(db, job.Id, manifest, token);
            result.Add(new(job.Id, manifest, level));
        }
        return result;
    }

    /// <summary>Checks ordered CEFR membership, never lexical ordering or an unrecognized level.</summary>
    internal static bool AllowsLevel(string maximum, string level) =>
        DocumentApprovalValues.CefrLevels.Contains(maximum, StringComparer.Ordinal)
        && DocumentApprovalValues.CefrLevels.TakeWhile(value => value != maximum).Append(maximum).Contains(level, StringComparer.Ordinal);

    private static bool CanReturn(DocumentMarkdownChunk chunk) =>
        chunk.Body.Length is > 0 and <= 8_000 && chunk.SourceSpans.Count is > 0 and <= 64;

    private static KnowledgePassageResponse Passage(DocumentKnowledgeGeneration generation, DocumentMarkdownChunk chunk) => new()
    {
        IndexJobId = generation.JobId,
        DocumentId = generation.Manifest.DocumentId,
        ChunkOrdinal = chunk.Ordinal,
        SourceSha256 = generation.Manifest.SourceSha256,
        CefrLevel = generation.CefrLevel,
        Text = chunk.Body,
        SourceSpans = chunk.SourceSpans.Select(span => new KnowledgeSourceSpan
        {
            PageNumber = span.PageNumber,
            BlockOrdinal = span.BlockOrdinal,
            StartOffset = span.StartOffset,
            EndOffset = span.EndOffset,
        }).ToArray(),
    };
}
