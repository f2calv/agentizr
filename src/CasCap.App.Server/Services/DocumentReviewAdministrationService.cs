#if DEBUG
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasCap.Services;

/// <summary>Manages receipt-derived projections for the authorized app; never downloads, prepares or approves documents.</summary>
public sealed class DocumentReviewAdministrationService
{
    private readonly IOptions<DocumentPreparationConfig> _config;
    private readonly ITenantContext _tenantContext;
    private readonly DocumentChunkingService _chunkingSvc;
    private readonly IDbContextFactory<AgentRuntimeDbContext>? _dbContextFactory;
    private readonly DocumentPreparationMarkdownStore? _markdownStore;
    private static readonly JsonSerializerOptions ReviewJsonOptions = new(JsonSerializerOptions.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal DocumentReviewAdministrationService(
        IOptions<DocumentPreparationConfig> config,
        ITenantContext tenantContext,
        DocumentChunkingService chunkingSvc,
        IDbContextFactory<AgentRuntimeDbContext>? dbContextFactory,
        DocumentPreparationMarkdownStore? markdownStore)
    {
        _config = config;
        _tenantContext = tenantContext;
        _chunkingSvc = chunkingSvc;
        _dbContextFactory = dbContextFactory;
        _markdownStore = markdownStore;
    }

    /// <summary>Previews current validated receipts, or reads one immutable generation while its source is current.</summary>
    public async Task<DocumentReviewGenerationOutcome> ReadAsync(
        string collectionId, string documentId, string jobId, string? generationId, CancellationToken token)
    {
        if (_dbContextFactory is null || _markdownStore is null)
            return new() { IsSupported = false };
        var source = await SourceAsync(collectionId, documentId, jobId, token);
        if (source is null)
            return new();
        var snapshot = generationId is null
            ? await _markdownStore.PreviewAsync(source, token)
            : await _markdownStore.ReadAsync(source, generationId, token);
        return new() { Generation = snapshot is null ? null : Response(source, snapshot, generationId) };
    }

    /// <summary>Appends or reuses a current-source review generation after validating bounded structural decisions.</summary>
    public async Task<DocumentReviewGenerationOutcome> StoreAsync(
        string collectionId, string documentId, string jobId, DocumentReviewGenerationRequest request, CancellationToken token)
    {
        if (_dbContextFactory is null || _markdownStore is null)
            return new() { IsSupported = false };
        var source = await SourceAsync(collectionId, documentId, jobId, token);
        if (source is null)
            return new();
        if (request.Sequencer != source.Sequencer || request.ETag != source.ETag)
            return new() { IsConflict = true };
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true))
            return new() { ValidationError = "Review source or chunking settings are invalid." };

        DocumentMarkdownSourceReview? review;
        try
        {
            review = request.Review is not { ValueKind: not JsonValueKind.Null } json
                ? null : json.Deserialize<DocumentMarkdownSourceReview>(ReviewJsonOptions)
                    ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new() { ValidationError = "Review must be a valid schema-2 source decision document." };
        }
        // Receipt failures are server errors, not invalid user decisions.
        var preview = await _markdownStore.PreviewAsync(source, token);
        if (preview is null)
            return new() { IsConflict = true };
        var settings = new DocumentMarkdownChunkingConfig
        {
            MaximumTokensPerChunk = request.MaximumTokensPerChunk,
            TargetTokensPerChunk = request.TargetTokensPerChunk,
            OverlapTokens = request.OverlapTokens,
        };
        try
        {
            _chunkingSvc.ChunkMarkdown(
                preview.Candidates.Select(item => new DocumentMarkdownPage(item.Page, item.Markdown)).ToArray(),
                settings, _config.Value, review, token);
        }
        catch (InvalidDataException)
        {
            return new() { ValidationError = "Review decisions do not match the source, structure or resource limits." };
        }
        var id = await _markdownStore.StoreAsync(source, settings, review, token);
        if (id is null)
            return new() { IsConflict = true };
        var stored = await _markdownStore.ReadAsync(source, id, token);
        return stored is null ? new() { IsConflict = true } : new() { Generation = Response(source, stored, id) };
    }

    private async Task<DocumentIngestionClaim?> SourceAsync(string collectionId, string documentId, string jobId, CancellationToken token)
    {
        await using var db = await _dbContextFactory!.CreateDbContextAsync(token);
        var tenant = _tenantContext.TenantId;
        var source = await SourceQuery(db, tenant, collectionId, documentId, jobId).SingleOrDefaultAsync(token);
        return source is null ? null : new()
        {
            JobId = source.JobId,
            TenantId = tenant,
            CollectionId = collectionId,
            DocumentId = documentId,
            Sequencer = source.Sequencer,
            ETag = source.ETag,
            BlobUri = source.BlobUri,
            PreparationPlanJson = source.PreparationPlanJson,
            ClaimId = Guid.Empty,
            ExpiresAtUtc = DateTimeOffset.MinValue,
        };
    }

    internal static IQueryable<DocumentReviewSource> SourceQuery(
        AgentRuntimeDbContext db, string tenant, string collectionId, string documentId, string jobId) =>
        from job in db.DocumentIngestionJobs.AsNoTracking()
        join document in db.KnowledgeDocuments.AsNoTracking() on job.DocumentId equals document.Id
        where job.TenantId == tenant && document.TenantId == tenant
            && job.CollectionId == collectionId && document.CollectionId == collectionId
            && job.DocumentId == documentId && job.Id == jobId
            && job.Sequencer == document.Sequencer && document.Status != DocumentIngestionStates.Deleted
            && job.Status == DocumentIngestionStates.ReviewRequired && job.ClaimId == null
            && job.FinishedAtUtc != null && job.PreparationPlanJson != null && document.ETag != null
        select new DocumentReviewSource(job.Id, job.Sequencer, job.PreparationPlanJson!, document.BlobUri, document.ETag!);

    private static DocumentReviewGenerationResponse Response(
        DocumentIngestionClaim source, DocumentMarkdownReviewGeneration snapshot, string? id) =>
        new()
        {
            GenerationId = id,
            State = snapshot.State,
            Sequencer = source.Sequencer,
            ETag = source.ETag!,
            SourceSha256 = snapshot.SourceSha256,
            Pages = snapshot.Candidates.Select(page => new DocumentReviewPageResponse
            {
                Page = page.Page,
                Markdown = page.Markdown,
                Changes = page.Changes,
                Uncertainties = page.Uncertainties,
            }).ToArray(),
            Projection = JsonSerializer.SerializeToElement(snapshot.Projection, JsonSerializerOptions.Web),
        };
}
#endif
