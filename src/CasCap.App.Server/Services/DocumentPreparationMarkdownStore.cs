using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Explicitly stores receipt-derived review candidates; never invokes providers or approves content.</summary>
// TODO: Expose only through authenticated tenant/reviewer management; receipts and annotation IDs are not reviewer authority.
// Keep visual and retrieval approval as separate gates before permitting embeddings or retrieval.
internal sealed class DocumentPreparationMarkdownStore(
    IOptions<DocumentPreparationConfig> config,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    DocumentChunkingService documentChunkingSvc)
{
    /// <summary>Returns the immutable generation ID, or null when the finished job/source is no longer reviewable.</summary>
    /// <remarks>The original claim pins ownership and source revision; its expired lease does not authorize this action.</remarks>
    public async Task<string?> StoreAsync(
        DocumentIngestionClaim source,
        DocumentMarkdownChunkingConfig settings,
        DocumentMarkdownSourceReview? review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        Validator.ValidateObject(config.Value, new ValidationContext(config.Value), validateAllProperties: true);
        await using var read = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(read, source).AnyAsync(cancellationToken))
            return null;
        var operations = await OperationsAsync(read, source, cancellationToken);
        var snapshot = Build(source, operations, settings, review, cancellationToken);
        var payload = JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web);
        var id = Identity(source, payload);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(cancellationToken))
            return null;
        var current = await OperationsAsync(db, source, cancellationToken);
        if (ReceiptBinding(operations) != ReceiptBinding(current))
            throw new InvalidOperationException("Validated preparation receipts changed while building the review generation.");
        var existing = await db.DocumentMarkdownGenerations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (existing is null)
        {
            db.DocumentMarkdownGenerations.Add(new()
            {
                Id = id,
                TenantId = source.TenantId,
                CollectionId = source.CollectionId,
                DocumentId = source.DocumentId,
                JobId = source.JobId,
                Sequencer = source.Sequencer,
                ETag = source.ETag!,
                ProjectionId = snapshot.Projection.Id,
                PayloadJson = payload,
                CreatedAtUtc = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else
            ValidateStored(source, existing);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(cancellationToken))
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Reads one exact current source's review generation; null means absent, superseded or deleted.</summary>
    public async Task<DocumentMarkdownReviewGeneration?> ReadAsync(DocumentIngestionClaim source, string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var available = DocumentIngestionTransactions.CurrentReviewSource(db, source);
        var item = await db.DocumentMarkdownGenerations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == id && item.TenantId == source.TenantId && item.CollectionId == source.CollectionId
            && item.DocumentId == source.DocumentId && item.JobId == source.JobId
            && item.Sequencer == source.Sequencer && item.ETag == source.ETag
            && available.Any(), cancellationToken);
        return item is null ? null : ValidateStored(source, item);
    }

    private async Task<DocumentPreparationOperationEntity[]> OperationsAsync(AgentRuntimeDbContext db, DocumentIngestionClaim source, CancellationToken token)
    {
        var query = db.DocumentPreparationOperations.AsNoTracking().Where(item => item.JobId == source.JobId
            && item.TenantId == source.TenantId && item.CollectionId == source.CollectionId && item.DocumentId == source.DocumentId)
            .OrderBy(item => item.Stage).ThenBy(item => item.PageNumber).Take(config.Value.MaximumPages + 2);
        // Npgsql JSONB members need a SQL text conversion that SQLite's string translator does not support.
        var lengthsQuery = db.Database.IsNpgsql()
            ? query.Select(item => item.ResultJson == null ? 0 : Convert.ToString(item.ResultJson)!.Length)
            : query.Select(item => item.ResultJson == null ? 0 : item.ResultJson.Length);
        var lengths = await lengthsQuery.ToArrayAsync(token);
        var maximum = 8L * config.Value.MaximumExtractedCharacters;
        if (lengths.Length < 2 || lengths.Length > config.Value.MaximumPages + 1
            || lengths.Any(length => length == 0) || lengths.Sum(length => (long)length) > maximum)
            throw new InvalidDataException("A review generation requires complete bounded preparation receipts.");
        var bounded = db.Database.IsNpgsql()
            ? query.Where(item => item.ResultJson != null && Convert.ToString(item.ResultJson)!.Length <= maximum)
            : query.Where(item => item.ResultJson != null && item.ResultJson.Length <= maximum);
        var operations = await bounded.ToArrayAsync(token);
        if (operations.Length != lengths.Length || operations.Sum(item => (long)item.ResultJson!.Length) > maximum)
            throw new InvalidDataException("Preparation receipt bounds changed while reading.");
        return operations;
    }

    private DocumentMarkdownReviewGeneration Build(
        DocumentIngestionClaim source,
        DocumentPreparationOperationEntity[] operations,
        DocumentMarkdownChunkingConfig settings,
        DocumentMarkdownSourceReview? review,
        CancellationToken token)
    {
        var plan = JsonSerializer.Deserialize<DocumentPreparationPlan>(source.PreparationPlanJson!, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The preparation plan is empty.");
        DocumentPreparationPlanService.Validate(plan.Definition, plan.Settings);
        var planHash = DocumentPreparationIdentity.Hash(source.PreparationPlanJson!);
        var sourceHash = operations[0].SourceSha256;
        if (plan.TenantId != source.TenantId || sourceHash.Length != 64 || !sourceHash.All(Uri.IsHexDigit)
            || operations.Any(item => item.Status != DocumentPreparationOperations.Validated || item.SourceSha256 != sourceHash
                || item.PlanSha256 != planHash || item.ResultSha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Review receipts must be validated and share the pinned source and plan.");
        var managed = operations.Where(item => item.Stage == DocumentPreparationOperations.DocumentIntelligence && item.PageNumber == 0).ToArray();
        if (managed.Length != 1)
            throw new InvalidDataException("Exactly one validated managed receipt is required.");
        var layout = JsonSerializer.Deserialize<DocumentLayoutPollResult>(managed[0].ResultJson!, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The managed receipt is empty.");
        if (layout.Pages is null)
            throw new InvalidDataException("The managed receipt has no physical-page map.");
        var pages = layout.Pages.Keys.Order().ToArray();
        if (layout.Status != "Succeeded" || layout.Markdown is null || layout.Markdown.Length > config.Value.MaximumExtractedCharacters
            || layout.Pages.Values.Any(page => page is null)
            || layout.Pages.Values.Sum(page => (long)page.Length) > config.Value.MaximumExtractedCharacters || pages.Length == 0
            || pages.Length > Math.Min(plan.Settings.MaximumPages, config.Value.MaximumPages)
            || pages.Any(page => page < 1 || page > config.Value.MaximumPages)
            || plan.Settings.PhysicalPages.Length != 0 && !pages.SequenceEqual(plan.Settings.PhysicalPages.Order()))
            throw new InvalidDataException("The managed receipt does not match the admitted physical pages.");
        var agent = operations.Where(item => item.Stage == DocumentPreparationOperations.PreparationAgent).OrderBy(item => item.PageNumber).ToArray();
        if (operations.Length != agent.Length + 1 || !pages.SequenceEqual(agent.Select(item => item.PageNumber)))
            throw new InvalidDataException("Every admitted physical page requires exactly one validated preparation receipt.");
        var candidates = agent.Select(item => Candidate(item, plan)).ToArray();
        if (candidates.Sum(item => (long)item.Markdown.Length + item.Changes.Sum(note => (long)note.Length)
            + item.Uncertainties.Sum(note => (long)note.Length)) > config.Value.MaximumExtractedCharacters)
            throw new InvalidDataException("Candidate transcription and review notes exceed the generation character limit.");
        var projection = documentChunkingSvc.ChunkMarkdown(candidates.Select(item => new DocumentMarkdownPage(item.Page, item.Markdown)).ToArray(),
            settings, config.Value, review, token);
        return new(1, sourceHash, planHash, operations.Select(item =>
            new DocumentMarkdownReceiptReference(item.Id, item.Stage, item.PageNumber, item.ResultSha256!)).ToArray(), candidates, projection);
    }

    private DocumentPreparationCandidate Candidate(DocumentPreparationOperationEntity operation, DocumentPreparationPlan plan)
    {
        var receipt = JsonSerializer.Deserialize<DocumentPreparationAgentReceipt>(operation.ResultJson!, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The preparation receipt is empty.");
        if (receipt.FailureCode is not null || receipt.Response?.Text is null)
            throw new InvalidDataException("A rejected preparation response cannot become a review generation.");
        return DocumentPreparationCandidateValidator.Validate(receipt.Response, plan, operation.PageNumber, config.Value.MaximumExtractedCharacters);
    }

    private static string ReceiptBinding(IEnumerable<DocumentPreparationOperationEntity> operations) =>
        DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(operations.Select(item =>
            new { item.Id, item.SourceSha256, item.PlanSha256, item.Stage, item.PageNumber, item.Status, item.ResultSha256, item.ResultJson })));

    private static string Identity(DocumentIngestionClaim source, string payload) =>
        DocumentPreparationIdentity.Digest(source.TenantId, source.CollectionId, source.DocumentId, source.JobId,
            source.Sequencer, source.ETag, DocumentPreparationIdentity.Hash(payload));

    private static DocumentMarkdownReviewGeneration ValidateStored(DocumentIngestionClaim source, DocumentMarkdownGenerationEntity item)
    {
        if (item.TenantId != source.TenantId || item.CollectionId != source.CollectionId
            || item.DocumentId != source.DocumentId || item.JobId != source.JobId
            || item.Sequencer != source.Sequencer || item.ETag != source.ETag)
            throw new InvalidDataException("The stored review generation has inconsistent ownership or source metadata.");
        var snapshot = JsonSerializer.Deserialize<DocumentMarkdownReviewGeneration>(item.PayloadJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The stored review generation is empty.");
        if (snapshot.Version != 1 || snapshot.Projection?.Profile is null || snapshot.Projection.Chunks is null
            || snapshot.Candidates is null || snapshot.Receipts is null
            || snapshot.Projection.Review is { } review && (review.Exclusions is null || review.Headings is null || review.LayoutTables is null)
            || item.ProjectionId != snapshot.Projection.Id
            || item.Id != Identity(source, JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web)))
            throw new InvalidDataException("The stored review generation does not match its immutable identity.");
        return snapshot;
    }
}
