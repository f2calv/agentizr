using Azure;
using CasCap.Constants;
using CasCap.Entities;
using System.ClientModel;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Writer;

namespace CasCap.Services;

/// <summary>Produces review-only hybrid candidates with one durable intent per paid stage/page and no implicit replay.</summary>
internal sealed class HybridDocumentProcessor(
    ILogger<HybridDocumentProcessor> logger,
    IOptions<DocumentPreparationConfig> config,
    TimeProvider timeProvider,
    DocumentBlobDownloadService documentBlobDownloadSvc,
    PdfDocumentPreparationService pdfDocumentPreparationSvc,
    DocumentPreparationOperationStore documentPreparationOperationStore,
    IDocumentPreparationProvider documentPreparationProvider) : IDocumentProcessor
{
    /// <inheritdoc/>
    public async Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (claim.PreparationPlanJson is null || string.IsNullOrWhiteSpace(claim.ETag))
            return Stop(DocumentProcessingOutcome.PermanentFailure);
        var plan = JsonSerializer.Deserialize<DocumentPreparationPlan>(claim.PreparationPlanJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The admitted preparation plan is empty.");
        if (plan.TenantId != claim.TenantId)
            throw new InvalidOperationException("The preparation plan belongs to a different tenant.");
        DocumentPreparationPlanService.Validate(plan.Definition, plan.Settings);
        try
        {
            using var bytes = new MemoryStream();
            Stream source;
            try
            {
                source = await documentBlobDownloadSvc.OpenReadAsync(claim, cancellationToken);
            }
            catch (ArgumentException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Stop(DocumentProcessingOutcome.PermanentFailure);
            }
            await using (source)
                await pdfDocumentPreparationSvc.ReadBoundedAsync(source, bytes, cancellationToken);
            if (bytes.Length > Math.Min(config.Value.MaximumPdfBytes, 26_214_400) || bytes.Length < 5
                || !bytes.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                return Stop(DocumentProcessingOutcome.PermanentFailure);
            var original = bytes.ToArray();
            using var pdf = PdfDocument.Open(original, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false });
            if (pdf.IsEncrypted || pdf.NumberOfPages < 1
                || pdf.NumberOfPages > Math.Min(config.Value.MaximumPages, plan.Settings.MaximumPages))
                return Stop(DocumentProcessingOutcome.PermanentFailure);
            var sourceHash = DocumentPreparationIdentity.Hash(original);
            var planHash = DocumentPreparationIdentity.Hash(claim.PreparationPlanJson);
            var pages = Enumerable.Range(1, pdf.NumberOfPages).ToArray();
            var managed = await PrepareLayoutAsync(new LayoutRequest(claim, plan, original, sourceHash, planHash, pages), cancellationToken);
            if (managed.Outcome is { } outcome)
                return Stop(outcome);
            var characters = 0;
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var builder = new PdfDocumentBuilder();
                builder.AddPage(pdf, page);
                var request = new DocumentPreparationPageRequest
                {
                    TenantId = claim.TenantId,
                    Plan = plan,
                    PageNumber = page,
                    Pdf = builder.Build(),
                    OriginalSha256 = sourceHash,
                    Draft = managed.Result!.Pages[page],
                };
                if (request.Pdf.Length > Math.Min(config.Value.MaximumPdfBytes, 26_214_400))
                    return Stop(DocumentProcessingOutcome.PermanentFailure);
                var prepared = await PreparePageAsync(claim, request, planHash, cancellationToken);
                if (prepared.Outcome is { } pageOutcome)
                    return Stop(pageOutcome);
                characters = checked(characters + prepared.Candidate!.Markdown.Length);
                if (characters > config.Value.MaximumExtractedCharacters)
                    return Stop(DocumentProcessingOutcome.PermanentFailure);
            }
            return DocumentProcessingOutcome.ReviewRequired;
        }
        catch (PdfDocumentEncryptedException)
        {
            return Stop(DocumentProcessingOutcome.PermanentFailure);
        }
        catch (PdfDocumentFormatException)
        {
            return Stop(DocumentProcessingOutcome.PermanentFailure);
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Stop(DocumentProcessingOutcome.PermanentFailure);
        }
        catch (Exception exception) when (exception is HttpRequestException or ClientResultException or TimeoutException
            || exception is RequestFailedException { Status: 408 or 429 or >= 500 and <= 599 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Stop(await FailureOutcomeAsync(claim, retryable: true, cancellationToken));
        }
        catch (InvalidDataException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Stop(await FailureOutcomeAsync(claim, retryable: false, cancellationToken));
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Stop(await FailureOutcomeAsync(claim, retryable: true, cancellationToken));
        }
    }

    private async Task<(DocumentLayoutPollResult? Result, DocumentProcessingOutcome? Outcome)> PrepareLayoutAsync(
        LayoutRequest request, CancellationToken cancellationToken)
    {
        var identity = new DocumentPreparationRequestIdentity(request.SourceHash, request.PlanHash,
            DocumentPreparationIdentity.Digest(request.SourceHash, request.PlanHash,
                AzDocumentLayoutClient.ApiVersion, AzDocumentLayoutClient.ModelId, "markdown", "utf16CodeUnit",
                string.Join(',', request.Pages)), DocumentPreparationOperations.DocumentIntelligence, 0);
        await VerifyAsync(request.Plan, cancellationToken);
        var reservation = await documentPreparationOperationStore.ReserveAsync(request.Claim, identity, cancellationToken);
        if (reservation is null)
            return (null, DocumentProcessingOutcome.ClaimLost);
        var operation = reservation.Operation;
        if (operation.Status == DocumentPreparationOperations.Rejected)
            return (null, DocumentProcessingOutcome.PermanentFailure);
        if (operation.ResultJson is { } json)
        {
            var saved = Read<DocumentLayoutPollResult>(json);
            if (saved.Status != "Succeeded" || saved.Markdown is null || !saved.Pages.Keys.Order().SequenceEqual(request.Pages))
                throw new InvalidDataException("The saved managed result does not match the admitted physical pages.");
            await documentPreparationOperationStore.ValidateAsync(operation);
            return (saved, null);
        }
        if (reservation.IsNew)
        {
            var uri = await WithBudgetAsync(request.Plan,
                token => documentPreparationProvider.SubmitLayoutAsync(request.Plan, request.Pdf, request.Pages, token), cancellationToken);
            await documentPreparationOperationStore.RecordOperationAsync(operation, uri);
        }
        if (operation.OperationUri is null)
            return (null, DocumentProcessingOutcome.ReconciliationRequired);
        while (true)
        {
            var result = await WithBudgetAsync(request.Plan, token => documentPreparationProvider.PollLayoutAsync(
                request.Plan, new Uri(operation.OperationUri), request.Pages, token), cancellationToken);
            if (result.Status == "Running")
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(result.RetryAfter?.TotalSeconds ?? 2, 1, 30)),
                    timeProvider, cancellationToken);
                continue;
            }
            if (result.Status != "Succeeded" || result.Markdown is null
                || result.Markdown.Length > config.Value.MaximumExtractedCharacters
                || !result.Pages.Keys.Order().SequenceEqual(request.Pages)
                || result.Pages.Values.Sum(page => (long)page.Length) > config.Value.MaximumExtractedCharacters)
            {
                await documentPreparationOperationStore.RecordResponseAsync(operation,
                    JsonSerializer.Serialize(new { FailureCode = "ManagedResultRejected", Status = result.Status }, JsonSerializerOptions.Web),
                    ManagedUsage(result));
                await documentPreparationOperationStore.RejectAsync(operation);
                return (null, DocumentProcessingOutcome.PermanentFailure);
            }
            await documentPreparationOperationStore.RecordResponseAsync(operation,
                JsonSerializer.Serialize(result, JsonSerializerOptions.Web),
                ManagedUsage(result));
            await documentPreparationOperationStore.ValidateAsync(operation);
            return (result, null);
        }
    }

    private async Task<(DocumentPreparationCandidate? Candidate, DocumentProcessingOutcome? Outcome)> PreparePageAsync(
        DocumentIngestionClaim claim, DocumentPreparationPageRequest request, string planHash, CancellationToken cancellationToken)
    {
        var identity = new DocumentPreparationRequestIdentity(request.OriginalSha256, planHash,
            DocumentPreparationIdentity.Digest(request.OriginalSha256, planHash,
                "pdfpig-physical-page-v1", typeof(PdfDocumentBuilder).Assembly.GetName().Version!.ToString(),
                DocumentPreparationIdentity.Hash(request.Draft),
                request.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            DocumentPreparationOperations.PreparationAgent, request.PageNumber, DocumentPreparationIdentity.Hash(request.Pdf));
        await VerifyAsync(request.Plan, cancellationToken);
        var reservation = await documentPreparationOperationStore.ReserveAsync(claim, identity, cancellationToken);
        if (reservation is null)
            return (null, DocumentProcessingOutcome.ClaimLost);
        var operation = reservation.Operation;
        if (operation.Status == DocumentPreparationOperations.Rejected)
            return (null, DocumentProcessingOutcome.PermanentFailure);
        if (operation.ResultJson is null && !reservation.IsNew)
            return (null, DocumentProcessingOutcome.ReconciliationRequired);
        if (reservation.IsNew)
        {
            try
            {
                await WithBudgetAsync(request.Plan, async token =>
                {
                    await documentPreparationProvider.ExecutePageAsync(request, async (response, _) =>
                    {
                        var tooLarge = response.Text.Length > config.Value.MaximumExtractedCharacters;
                        var receipt = new DocumentPreparationAgentReceipt
                        {
                            Response = tooLarge ? response with { Text = string.Empty } : response,
                            FailureCode = tooLarge ? "ResponseSizeExceeded" : null,
                        };
                        await documentPreparationOperationStore.RecordResponseAsync(operation,
                            JsonSerializer.Serialize(receipt, JsonSerializerOptions.Web),
                            response.Usage is null ? null : JsonSerializer.Serialize(response.Usage, JsonSerializerOptions.Web));
                    }, token);
                    return true;
                }, cancellationToken);
            }
            catch (InvalidOperationException) when (operation.ResultJson is not null)
            {
                logger.LogWarning("The framework rejected a preparation response; its receipt will be locally validated.");
            }
        }
        if (operation.ResultJson is null)
            return (null, DocumentProcessingOutcome.ReconciliationRequired);
        try
        {
            var receipt = Read<DocumentPreparationAgentReceipt>(operation.ResultJson);
            if (receipt.FailureCode is not null)
                throw new InvalidDataException("The preparation response exceeded its durable projection limit.");
            var candidate = DocumentPreparationCandidateValidator.Validate(
                receipt.Response, request.Plan, request.PageNumber, config.Value.MaximumExtractedCharacters);
            await documentPreparationOperationStore.ValidateAsync(operation);
            return (candidate, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            await documentPreparationOperationStore.RejectAsync(operation);
            return (null, DocumentProcessingOutcome.PermanentFailure);
        }
    }

    private async Task VerifyAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken) =>
        await WithBudgetAsync(plan, async token =>
        {
            await documentPreparationProvider.VerifyModelAsync(plan, token);
            return true;
        }, cancellationToken);

    private async Task<T> WithBudgetAsync<T>(
        DocumentPreparationPlan plan, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(plan.Settings.RequestTimeoutSeconds), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            return await action(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The preparation provider exceeded the admitted request deadline.");
        }
    }

    private async Task<DocumentProcessingOutcome> FailureOutcomeAsync(
        DocumentIngestionClaim claim, bool retryable, CancellationToken cancellationToken) =>
        (await documentPreparationOperationStore.ReadAsync(claim, cancellationToken))
            .Any(operation => operation.Status == DocumentPreparationOperations.Submitting)
                ? DocumentProcessingOutcome.ReconciliationRequired
                : retryable ? DocumentProcessingOutcome.RetryableFailure : DocumentProcessingOutcome.PermanentFailure;

    private DocumentProcessingOutcome Stop(DocumentProcessingOutcome outcome)
    {
        logger.LogWarning("Hybrid document preparation stopped with {Outcome}; any paid receipts remain retained.", outcome);
        return outcome;
    }

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The retained preparation projection is empty.");

    private static string? ManagedUsage(DocumentLayoutPollResult result) =>
        result.Status == "Succeeded"
            ? JsonSerializer.Serialize(new { analyzedPages = result.Pages.Count, basis = "returned-physical-pages" })
            : null;

    private sealed record LayoutRequest(
        DocumentIngestionClaim Claim, DocumentPreparationPlan Plan, byte[] Pdf, string SourceHash, string PlanHash, int[] Pages);
}
