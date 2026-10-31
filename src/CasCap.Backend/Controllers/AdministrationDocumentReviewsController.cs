#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using CasCap.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes source-fenced review and explicit indexing authorization to signed-in assigned administrators.</summary>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[Authorize(Policy = AdministrationPolicies.AuthenticatedDocumentReviewer)]
[ValidateAdministrationAntiforgery]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status501NotImplemented)]
public sealed class AdministrationDocumentReviewsController : ControllerBase
{
    /// <inheritdoc cref="DocumentIndexingAdministrationService.AcceptAsync"/>
    [HttpPost(DocumentReviewRoutes.IndexingJobs, Name = "RequestDocumentIndexing")]
    [RequestSizeLimit(16_384)]
    public async Task<Results<Accepted<DocumentIndexingJobResponse>, NotFound, Conflict, ValidationProblem, StatusCodeHttpResult>> RequestIndexingAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [FromBody] DocumentIndexingRequest request,
        [FromServices] DocumentIndexingAdministrationService indexingSvc,
        CancellationToken cancellationToken) =>
        (await indexingSvc.AcceptAsync(collectionId, documentId, jobId, generationId, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } error } => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request)] = [error] }),
            { Job: { } job } => TypedResults.Accepted($"{Request.PathBase}{Request.Path}/{Uri.EscapeDataString(job.Id)}", job),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="DocumentIndexingAdministrationService.ReadAsync"/>
    [HttpGet(DocumentReviewRoutes.IndexingJob, Name = "GetDocumentIndexingJob")]
    public async Task<Results<Ok<DocumentIndexingJobResponse>, NotFound, StatusCodeHttpResult>> ReadIndexingAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [StringLength(64, MinimumLength = 1)] string indexingJobId,
        [FromServices] DocumentIndexingAdministrationService indexingSvc,
        CancellationToken cancellationToken) =>
        (await indexingSvc.ReadAsync(collectionId, documentId, jobId, generationId, indexingJobId, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { Job: { } job } => TypedResults.Ok(job),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="DocumentReviewAdministrationService.PreflightIndexingAsync"/>
    [HttpGet(DocumentReviewRoutes.IndexingPreflight, Name = "PreflightDocumentIndexing")]
    public async Task<Results<Ok<DocumentIndexingPreflightResponse>, NotFound, StatusCodeHttpResult>> PreflightIndexingAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.PreflightIndexingAsync(collectionId, documentId, jobId, generationId, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { Preflight: { } result } => TypedResults.Ok(result),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="DocumentReviewAdministrationService.RecordApprovalAsync"/>
    [HttpPut(DocumentReviewRoutes.Approval, Name = "RecordDocumentApproval")]
    [RequestSizeLimit(16_384)]
    public async Task<Results<Ok<DocumentReviewGenerationResponse>, NotFound, Conflict, ValidationProblem, StatusCodeHttpResult>> RecordApprovalAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [FromBody] DocumentApprovalRequest request,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.RecordApprovalAsync(collectionId, documentId, jobId, generationId, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } error } => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request)] = [error] }),
            { Generation: { } generation } => TypedResults.Ok(generation),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="DocumentReviewAdministrationService.RecordChunkReviewAsync"/>
    [HttpPut(DocumentReviewRoutes.ChunkReview, Name = "RecordDocumentChunkReview")]
    [RequestSizeLimit(1_048_576)]
    public async Task<Results<Ok<DocumentReviewGenerationResponse>, NotFound, Conflict, ValidationProblem, StatusCodeHttpResult>> RecordChunkReviewAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [FromBody] DocumentChunkReviewRequest request,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.RecordChunkReviewAsync(collectionId, documentId, jobId, generationId, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } error } => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request)] = [error] }),
            { Generation: { } generation } => TypedResults.Ok(generation),
            _ => TypedResults.NotFound(),
        };

    /// <summary>Previews validated source Markdown and heading-aware chunks without persisting or preparing anything.</summary>
    [HttpGet(DocumentReviewRoutes.Source, Name = "PreviewDocumentReview")]
    public async Task<Results<Ok<DocumentReviewGenerationResponse>, NotFound, StatusCodeHttpResult>> PreviewAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.ReadAsync(collectionId, documentId, jobId, null, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { Generation: { } generation } => TypedResults.Ok(generation),
            _ => TypedResults.NotFound(),
        };

    /// <summary>Reads one immutable review generation only while its exact tenant/job/source remains current.</summary>
    [HttpGet(DocumentReviewRoutes.Generation, Name = "GetDocumentReviewGeneration")]
    public async Task<Results<Ok<DocumentReviewGenerationResponse>, NotFound, StatusCodeHttpResult>> ReadAsync(
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [StringLength(128, MinimumLength = 1)] string generationId,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.ReadAsync(collectionId, documentId, jobId, generationId, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { Generation: { } generation } => TypedResults.Ok(generation),
            _ => TypedResults.NotFound(),
        };

    /// <summary>Stores an immutable review-required generation; retries reuse its identity and never approve indexing.</summary>
    [HttpPost(DocumentReviewRoutes.Generations, Name = "StoreDocumentReviewGeneration")]
    [RequestSizeLimit(1_048_576)]
    public async Task<Results<Created<DocumentReviewGenerationResponse>, NotFound, Conflict, ValidationProblem, StatusCodeHttpResult>> StoreAsync(
        string appId,
        [StringLength(200, MinimumLength = 1)] string collectionId,
        [StringLength(128, MinimumLength = 1)] string documentId,
        [StringLength(128, MinimumLength = 1)] string jobId,
        [FromBody] DocumentReviewGenerationRequest request,
        [FromServices] DocumentReviewAdministrationService reviewSvc,
        CancellationToken cancellationToken) =>
        (await reviewSvc.StoreAsync(collectionId, documentId, jobId, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } error } => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request)] = [error] }),
            { Generation: { } generation } => TypedResults.Created(
                DocumentReviewRoutes.Format(DocumentReviewRoutes.Generation, appId, collectionId, documentId, jobId, generation.GenerationId),
                generation),
            _ => TypedResults.NotFound(),
        };
}
#endif
