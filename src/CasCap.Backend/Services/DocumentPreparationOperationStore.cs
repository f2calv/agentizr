using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CasCap.Services;

/// <summary>Commits paid intents before dispatch and receipts independently of claim expiry, without activating output.</summary>
internal sealed class DocumentPreparationOperationStore(
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory)
{
    /// <summary>Returns null for a lost claim; an existing operation can never authorize another POST.</summary>
    public async Task<DocumentPreparationReservation?> ReserveAsync(
        DocumentIngestionClaim claim,
        DocumentPreparationRequestIdentity request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (claim.PreparationPlanJson is null
            || request.PlanSha256 != DocumentPreparationIdentity.Hash(claim.PreparationPlanJson)
            || !IsDigest(request.SourceSha256) || !IsDigest(request.RequestSha256)
            || request.DerivedPdfSha256 is { } derived && !IsDigest(derived)
            || !(request.Stage == DocumentPreparationOperations.DocumentIntelligence && request.PageNumber == 0
                || request.Stage == DocumentPreparationOperations.PreparationAgent && request.PageNumber is >= 1 and <= 200))
            throw new InvalidOperationException("A paid reservation requires a valid admission-bound stage and request identity.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken);
        if (!await DocumentIngestionTransactions.CurrentClaim(db, claim, now).AnyAsync(cancellationToken))
            return null;
        var operation = await Scope(db, claim).SingleOrDefaultAsync(
            item => item.Stage == request.Stage && item.PageNumber == request.PageNumber, cancellationToken);
        var isNew = operation is null;
        if (operation is null)
        {
            operation = new DocumentPreparationOperationEntity
            {
                Id = DocumentPreparationIdentity.Digest(claim.TenantId, claim.CollectionId, claim.JobId,
                    request.Stage, request.PageNumber.ToString(CultureInfo.InvariantCulture)),
                JobId = claim.JobId,
                TenantId = claim.TenantId,
                CollectionId = claim.CollectionId,
                DocumentId = claim.DocumentId,
                SourceSha256 = request.SourceSha256,
                PlanSha256 = request.PlanSha256,
                RequestSha256 = request.RequestSha256,
                DerivedPdfSha256 = request.DerivedPdfSha256,
                Stage = request.Stage,
                PageNumber = request.PageNumber,
                SubmissionClaimId = claim.ClaimId,
                Status = DocumentPreparationOperations.Submitting,
                CreatedAtUtc = now,
            };
            db.DocumentPreparationOperations.Add(operation);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (operation.SourceSha256 != request.SourceSha256 || operation.PlanSha256 != request.PlanSha256
            || operation.RequestSha256 != request.RequestSha256)
            throw new InvalidOperationException("The pinned preparation request changed; paid replay is not authorized.");
        await transaction.CommitAsync(cancellationToken);
        return new(operation, isNew);
    }

    /// <summary>Persists the validated managed operation receipt; callers must never retry its submission.</summary>
    public Task RecordOperationAsync(DocumentPreparationOperationEntity expected, Uri operation) =>
        WriteReceiptAsync(expected, item =>
        {
            if (item.OperationUri is not null && item.OperationUri != operation.AbsoluteUri)
                throw new InvalidOperationException("A managed operation receipt cannot be replaced.");
            item.OperationUri = operation.AbsoluteUri;
            if (item.Status == DocumentPreparationOperations.Submitting)
                item.Status = DocumentPreparationOperations.Polling;
        });

    /// <summary>Persists a bounded explicit response and native usage before any validation or continuation.</summary>
    public Task RecordResponseAsync(DocumentPreparationOperationEntity expected, string resultJson, string? usageJson) =>
        WriteReceiptAsync(expected, item =>
        {
            var digest = DocumentPreparationIdentity.Hash(resultJson);
            if (item.ResultSha256 is not null)
            {
                if (item.ResultSha256 != digest)
                    throw new InvalidOperationException("A paid response receipt cannot be replaced.");
                return;
            }
            if (item.Status == DocumentPreparationOperations.Rejected)
                throw new InvalidOperationException("A rejected paid operation cannot be overwritten.");
            item.ResultJson = resultJson;
            item.ResultSha256 = digest;
            item.UsageJson = usageJson;
            item.Status = DocumentPreparationOperations.Received;
        });

    /// <summary>Marks a recorded response as syntactically valid, never searchable or semantically approved.</summary>
    public Task ValidateAsync(DocumentPreparationOperationEntity expected) =>
        WriteReceiptAsync(expected, item =>
        {
            if (item.Status == DocumentPreparationOperations.Received)
                item.Status = DocumentPreparationOperations.Validated;
            else if (item.Status != DocumentPreparationOperations.Validated)
                throw new InvalidOperationException("Only a recorded response can become a validated candidate.");
        });

    /// <summary>Retains an explicit failed/rejected operation rather than silently permitting another charge.</summary>
    public Task RejectAsync(DocumentPreparationOperationEntity expected) =>
        WriteReceiptAsync(expected, item =>
        {
            if (item.Status == DocumentPreparationOperations.Validated)
                throw new InvalidOperationException("A validated candidate cannot be overwritten.");
            item.Status = DocumentPreparationOperations.Rejected;
        });

    /// <summary>Reads only one claim's tenant/document-qualified operation projections.</summary>
    public async Task<IReadOnlyList<DocumentPreparationOperationEntity>> ReadAsync(
        DocumentIngestionClaim claim,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await Scope(db, claim).AsNoTracking().OrderBy(item => item.PageNumber).ToArrayAsync(cancellationToken);
    }

    private async Task WriteReceiptAsync(
        DocumentPreparationOperationEntity expected,
        Action<DocumentPreparationOperationEntity> mutate)
    {
        // A receipt remains an accounting fact after cancellation/claim loss; this bounded write cannot activate output.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
        var token = deadline.Token;
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var item = await db.DocumentPreparationOperations.SingleAsync(item => item.Id == expected.Id
            && item.JobId == expected.JobId && item.TenantId == expected.TenantId
            && item.CollectionId == expected.CollectionId && item.DocumentId == expected.DocumentId
            && item.SubmissionClaimId == expected.SubmissionClaimId
            && item.SourceSha256 == expected.SourceSha256
            && item.RequestSha256 == expected.RequestSha256 && item.PlanSha256 == expected.PlanSha256, token);
        mutate(item);
        item.ReceivedAtUtc ??= await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        expected.Status = item.Status;
        expected.OperationUri = item.OperationUri;
        expected.ResultJson = item.ResultJson;
        expected.ResultSha256 = item.ResultSha256;
        expected.UsageJson = item.UsageJson;
    }

    private static IQueryable<DocumentPreparationOperationEntity> Scope(AgentRuntimeDbContext db, DocumentIngestionClaim claim) =>
        db.DocumentPreparationOperations.Where(item => item.JobId == claim.JobId && item.TenantId == claim.TenantId
            && item.CollectionId == claim.CollectionId && item.DocumentId == claim.DocumentId);

    private static bool IsDigest(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
