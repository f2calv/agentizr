using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CasCap.Client.Services;

/// <summary>Calls the same-origin administration API and maps every HTTP outcome to a typed result.</summary>
/// <remarks>Mutations carry the session's antiforgery token. Nothing here retries a mutation after an unknown outcome.</remarks>
public sealed class AdministrationApiClient(HttpClient httpClient, AdministrationSessionState sessionState)
{
    /// <summary>Loads the session description and caches it, including its antiforgery token.</summary>
    public async Task<ApiResult<AdministrationSessionResponse>> GetSessionAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<AdministrationSessionResponse>(
            new HttpRequestMessage(HttpMethod.Get, AdministrationRoutes.Session),
            cancellationToken);
        if (result.IsSuccess)
            sessionState.Current = result.Value;
        return result;
    }

    /// <summary>Lists the apps the administrator may manage.</summary>
    public Task<ApiResult<IReadOnlyList<AppSummaryResponse>>> GetAppsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AppSummaryResponse>>(AdministrationRoutes.Apps, cancellationToken);

    /// <summary>Lists one bounded page of admitted sources and their current jobs, optionally within one exact collection.</summary>
    public Task<ApiResult<DocumentCataloguePageResponse>> GetDocumentsAsync(
        string appId, string? collection, int page, int pageSize, CancellationToken cancellationToken) =>
        GetAsync<DocumentCataloguePageResponse>(
            AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, appId)
                + $"?page={page}&pageSize={pageSize}"
                + (string.IsNullOrWhiteSpace(collection) ? string.Empty : $"&collection={Uri.EscapeDataString(collection)}"),
            cancellationToken);

    /// <summary>Reads source synchronization settings/progress without returning source coordinates.</summary>
    public Task<ApiResult<DocumentSynchronizationResponse>> GetDocumentSynchronizationAsync(
        string appId, CancellationToken cancellationToken) =>
        GetAsync<DocumentSynchronizationResponse>(
            AdministrationRoutes.Format(DocumentCatalogueRoutes.Synchronization, appId), cancellationToken);

    /// <summary>Persists one coalesced request with antiforgery protection; never retries an uncertain mutation.</summary>
    public Task<ApiResult<DocumentSynchronizationResponse>> RequestDocumentSynchronizationAsync(
        string appId, CancellationToken cancellationToken) =>
        SendMutationAsync<DocumentSynchronizationResponse>(HttpMethod.Post,
            AdministrationRoutes.Format(DocumentCatalogueRoutes.Synchronization, appId), new { }, cancellationToken);

    /// <summary>Previews a current source or reads an immutable review-required generation.</summary>
    public Task<ApiResult<DocumentReviewGenerationResponse>> GetDocumentReviewAsync(
        string appId, string collectionId, string documentId, string jobId, string? generationId, CancellationToken cancellationToken) =>
        GetAsync<DocumentReviewGenerationResponse>(
            DocumentReviewRoutes.Format(generationId is null ? DocumentReviewRoutes.Source : DocumentReviewRoutes.Generation,
                appId, collectionId, documentId, jobId, generationId), cancellationToken);

    /// <summary>Stores a source-fenced review generation with antiforgery protection; never retries or approves it.</summary>
    public Task<ApiResult<DocumentReviewGenerationResponse>> StoreDocumentReviewAsync(
        string appId, string collectionId, string documentId, string jobId,
        DocumentReviewGenerationRequest request, CancellationToken cancellationToken) =>
        SendMutationAsync<DocumentReviewGenerationResponse>(HttpMethod.Post,
            DocumentReviewRoutes.Format(DocumentReviewRoutes.Generations, appId, collectionId, documentId, jobId),
            request, cancellationToken);

    /// <summary>Records selection decisions with antiforgery and an observed revision; never retries or approves indexing.</summary>
    public Task<ApiResult<DocumentReviewGenerationResponse>> RecordDocumentChunkReviewAsync(
        string appId, string collectionId, string documentId, string jobId, string generationId,
        DocumentChunkReviewRequest request, CancellationToken cancellationToken) =>
        SendMutationAsync<DocumentReviewGenerationResponse>(HttpMethod.Put,
            DocumentReviewRoutes.Format(DocumentReviewRoutes.ChunkReview, appId, collectionId, documentId, jobId, generationId),
            request, cancellationToken);

    /// <summary>Records a document readiness assessment with antiforgery and observed revisions; never retries or indexes.</summary>
    public Task<ApiResult<DocumentReviewGenerationResponse>> RecordDocumentApprovalAsync(
        string appId, string collectionId, string documentId, string jobId, string generationId,
        DocumentApprovalRequest request, CancellationToken cancellationToken) =>
        SendMutationAsync<DocumentReviewGenerationResponse>(HttpMethod.Put,
            DocumentReviewRoutes.Format(DocumentReviewRoutes.Approval, appId, collectionId, documentId, jobId, generationId),
            request, cancellationToken);

    /// <summary>Gets one page of an app's agents.</summary>
    public Task<ApiResult<AgentSummaryPageResponse>> GetAgentsAsync(
        string appId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = $"?page={page}&pageSize={pageSize}"
            + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        return GetAsync<AgentSummaryPageResponse>(
            AdministrationRoutes.Format(AdministrationRoutes.Agents, appId) + query,
            cancellationToken);
    }

    /// <summary>Gets the app's provider registrations and definition policy.</summary>
    public Task<ApiResult<ProviderCatalogueResponse>> GetProvidersAsync(string appId, CancellationToken cancellationToken) =>
        GetAsync<ProviderCatalogueResponse>(AdministrationRoutes.Format(AdministrationRoutes.Providers, appId), cancellationToken);

    /// <summary>Gets an agent's newest-first published versions.</summary>
    public Task<ApiResult<IReadOnlyList<AgentDefinitionHistoryItemResponse>>> GetHistoryAsync(
        string appId,
        string agentName,
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AgentDefinitionHistoryItemResponse>>(
            AdministrationRoutes.Format(AdministrationRoutes.Definitions, appId, agentName),
            cancellationToken);

    /// <summary>Suggests an unused version, checking snapshots outside the bounded history window too.</summary>
    /// <remarks>A concurrent publication can still claim the suggestion; publication returns a conflict in that case.</remarks>
    public async Task<ApiResult<string>> GetSuggestedVersionAsync(
        string appId,
        string agentName,
        CancellationToken cancellationToken)
    {
        var history = await GetHistoryAsync(appId, agentName, cancellationToken);
        if (!history.IsSuccess)
            return new ApiResult<string>(history.Status, Errors: history.Errors);
        var existingVersions = history.Value!.Select(item => item.DefinitionVersion).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var version = DefinitionDraft.SuggestVersion(existingVersions);
            var snapshot = await GetVersionAsync(appId, agentName, version, cancellationToken);
            if (snapshot.Status == ApiStatus.NotFound)
                return new ApiResult<string>(ApiStatus.Success, version);
            if (!snapshot.IsSuccess)
                return new ApiResult<string>(snapshot.Status, Errors: snapshot.Errors);
            existingVersions.Add(version);
        }
    }

    /// <summary>Gets the authoritative active version, or a not-found outcome when the agent is inactive.</summary>
    public Task<ApiResult<AgentDefinitionSnapshotResponse>> GetActiveAsync(
        string appId,
        string agentName,
        CancellationToken cancellationToken) =>
        GetAsync<AgentDefinitionSnapshotResponse>(
            AdministrationRoutes.Format(AdministrationRoutes.ActiveDefinition, appId, agentName),
            cancellationToken);

    /// <summary>Gets an agent's newest-first activation, rollback and deactivation audit.</summary>
    public Task<ApiResult<IReadOnlyList<AgentDefinitionActivationResponse>>> GetActivationsAsync(
        string appId,
        string agentName,
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AgentDefinitionActivationResponse>>(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionActivations, appId, agentName),
            cancellationToken);

    /// <summary>Gets one immutable version.</summary>
    public Task<ApiResult<AgentDefinitionSnapshotResponse>> GetVersionAsync(
        string appId,
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken) =>
        GetAsync<AgentDefinitionSnapshotResponse>(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionVersion, appId, agentName, definitionVersion),
            cancellationToken);

    /// <summary>Publishes a new inactive version.</summary>
    public Task<ApiResult<AgentDefinitionSnapshotResponse>> PublishAsync(
        string appId,
        string agentName,
        PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        SendMutationAsync<AgentDefinitionSnapshotResponse>(
            HttpMethod.Post,
            AdministrationRoutes.Format(AdministrationRoutes.Definitions, appId, agentName),
            request,
            cancellationToken);

    /// <summary>Activates or rolls back to a version if the observed active version is still current.</summary>
    public Task<ApiResult<bool>> ActivateAsync(
        string appId,
        string agentName,
        string definitionVersion,
        AdministrationActivationRequest request,
        CancellationToken cancellationToken) =>
        SendMutationAsync<bool>(
            HttpMethod.Put,
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionActivation, appId, agentName, definitionVersion),
            request,
            cancellationToken);

    /// <summary>Deactivates the active version if it is still the observed version.</summary>
    public Task<ApiResult<bool>> DeactivateAsync(
        string appId,
        string agentName,
        AdministrationDeactivationRequest request,
        CancellationToken cancellationToken) =>
        SendMutationAsync<bool>(
            HttpMethod.Post,
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionDeactivation, appId, agentName),
            request,
            cancellationToken);

    private Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    private async Task<ApiResult<T>> SendMutationAsync<T>(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        if (sessionState.Current is null && !(await GetSessionAsync(cancellationToken)).IsSuccess)
            return new ApiResult<T>(ApiStatus.Unavailable, Errors: ["The administration session could not be loaded."]);
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
        request.Headers.Add(AdministrationRoutes.AntiforgeryHeaderName, sessionState.Current!.AntiforgeryToken);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted => new ApiResult<T>(
                        ApiStatus.Success,
                        await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken)),
                    HttpStatusCode.NoContent => new ApiResult<T>(ApiStatus.Success, typeof(T) == typeof(bool) ? (T)(object)true : default),
                    HttpStatusCode.BadRequest => await ReadProblemAsync<T>(response, cancellationToken),
                    HttpStatusCode.Unauthorized => new ApiResult<T>(ApiStatus.Unauthorized),
                    HttpStatusCode.Forbidden => new ApiResult<T>(ApiStatus.Forbidden),
                    HttpStatusCode.NotFound => new ApiResult<T>(ApiStatus.NotFound),
                    HttpStatusCode.Conflict => new ApiResult<T>(ApiStatus.Conflict),
                    HttpStatusCode.NotImplemented or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout => new ApiResult<T>(ApiStatus.Unavailable),
                    _ => new ApiResult<T>(ApiStatus.Failed),
                };
            }
            catch (HttpRequestException)
            {
                return new ApiResult<T>(ApiStatus.Unavailable);
            }
        }
    }

    private static async Task<ApiResult<T>> ReadProblemAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(JsonSerializerOptions.Web, cancellationToken);
            IReadOnlyList<string> messages = problem?.Errors?.SelectMany(error => error.Value).ToArray() is { Length: > 0 } errors
                ? errors
                : [problem?.Title ?? "The request was rejected."];
            return new(ApiStatus.ValidationFailed, Errors: messages) { FieldErrors = problem?.Errors };
        }
        catch (JsonException)
        {
            return new(ApiStatus.ValidationFailed, Errors: ["The request was rejected."]);
        }
    }

    private sealed record ProblemPayload(string? Title, Dictionary<string, string[]>? Errors);
}
