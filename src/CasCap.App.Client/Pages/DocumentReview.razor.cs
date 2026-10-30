using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Text.Json;

namespace CasCap.Client.Pages;

/// <summary>Shows authorized receipt-derived pages and chunks, and stores an explicitly confirmed review-only snapshot.</summary>
public partial class DocumentReview(
    AdministrationApiClient apiClient,
    NavigationManager navigationManager,
    IDialogService dialogService) : CancellableComponentBase
{
    private (string App, string Collection, string Document, string Job, string? Generation)? _loaded;
    private ApiResult<DocumentReviewGenerationResponse>? _result;
    private ApiResult<DocumentReviewGenerationResponse>? _writeResult;
    private DocumentReviewProjectionView? _projection;
    private bool _busy;
    private bool _selectionDirty;
    private bool _approvalDirty;
    private IReadOnlyDictionary<string, string[]>? _approvalFieldErrors;

    /// <summary>Gets or sets the requested app.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant-local collection.</summary>
    [Parameter]
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the source identity.</summary>
    [Parameter]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the finished review-required job.</summary>
    [Parameter]
    public string JobId { get; set; } = string.Empty;

    /// <summary>Gets or sets an exact immutable snapshot to read instead of previewing.</summary>
    [SupplyParameterFromQuery(Name = "generationId")]
    public string? GenerationId { get; set; }

    /// <summary>Builds a browser route with escaped domain identities and an optional immutable snapshot.</summary>
    public static string Path(string appId, string collectionId, string documentId, string jobId, string? generationId = null) =>
        $"apps/{Uri.EscapeDataString(appId)}/collections/{Uri.EscapeDataString(collectionId)}"
            + $"/documents/{Uri.EscapeDataString(documentId)}/jobs/{Uri.EscapeDataString(jobId)}/review"
            + (generationId is null ? string.Empty : $"?generationId={Uri.EscapeDataString(generationId)}");

    private List<BreadcrumbItem> Breadcrumbs =>
    [
        new("Apps", href: "/"),
        new(AppId, href: Apps.AppPath(AppId)),
        new("Documents", href: Documents.Path(AppId)),
        new("Review", href: null, disabled: true),
    ];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        var selection = (AppId, CollectionId, DocumentId, JobId, GenerationId);
        if (_loaded == selection)
            return;
        _loaded = selection;
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async token =>
    {
        _result = null;
        _writeResult = null;
        _projection = null;
        _selectionDirty = false;
        _approvalDirty = false;
        _approvalFieldErrors = null;
        var result = await apiClient.GetDocumentReviewAsync(AppId, CollectionId, DocumentId, JobId, GenerationId, token);
        token.ThrowIfCancellationRequested();
        _result = result;
        if (result.Value is { } review)
        {
            try
            {
                _projection = DocumentReviewProjectionView.Read(review.Projection);
            }
            catch (JsonException)
            {
                _projection = null;
            }
        }
    }, renew: true);

    private async Task ReloadAfterWriteFailureAsync()
    {
        if (_busy)
            return;
        if ((_selectionDirty || _approvalDirty) && await dialogService.ShowMessageBoxAsync("Discard unsaved decisions and refresh?",
            "Refresh loads the authoritative saved review. Your unsaved local edits will be discarded.",
            yesText: "Discard and refresh", cancelText: "Keep editing") != true)
            return;
        await ReloadAsync();
    }

    private async Task RecordSelectionAsync(IReadOnlyList<DocumentChunkExclusionRequest> exclusions)
    {
        if (_busy || _approvalDirty || _result?.Value is not { GenerationId: { } id } review || _projection is null)
            return;
        var selection = _loaded;
        var confirmed = await dialogService.ShowMessageBoxAsync("Save chunk selection review?",
            "This records an immutable review revision. It does not change the PDF or candidate text, approve indexing, or run providers.",
            yesText: "Save review", cancelText: "Cancel");
        if (confirmed != true || selection != _loaded)
            return;
        _busy = true;
        try
        {
            await RunAsync(async token =>
            {
                var result = await apiClient.RecordDocumentChunkReviewAsync(AppId, CollectionId, DocumentId, JobId, id,
                    new DocumentChunkReviewRequest
                    {
                        Sequencer = review.Sequencer,
                        ETag = review.ETag,
                        ExpectedRevision = review.ChunkReview?.Revision ?? 0,
                        Exclusions = exclusions,
                    }, token);
                token.ThrowIfCancellationRequested();
                _writeResult = result;
                if (result is { IsSuccess: true, Value: { } saved })
                {
                    _result = result;
                    _projection = DocumentReviewProjectionView.Read(saved.Projection);
                    _selectionDirty = false;
                }
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task StoreAsync()
    {
        if (_busy || _result?.Value is not { GenerationId: null } review || _projection is not { } projection)
            return;
        var selection = _loaded;
        var confirmed = await dialogService.ShowMessageBoxAsync(
            "Store review-required snapshot?",
            "This stores an immutable projection of the observed source. It does not approve content, invoke preparation or enable search.",
            yesText: "Store snapshot", cancelText: "Cancel");
        if (confirmed != true || selection != _loaded)
            return;
        _busy = true;
        try
        {
            await RunAsync(async token =>
            {
                _writeResult = await apiClient.StoreDocumentReviewAsync(
                    AppId, CollectionId, DocumentId, JobId,
                    new DocumentReviewGenerationRequest
                    {
                        Sequencer = review.Sequencer,
                        ETag = review.ETag,
                        MaximumTokensPerChunk = projection.Profile.MaximumTokensPerChunk,
                        TargetTokensPerChunk = projection.Profile.TargetTokensPerChunk,
                        OverlapTokens = projection.Profile.OverlapTokens,
                        Review = projection.Review,
                    }, token);
                token.ThrowIfCancellationRequested();
                if (_writeResult is { IsSuccess: true, Value.GenerationId: { } id })
                    navigationManager.NavigateTo(Path(AppId, CollectionId, DocumentId, JobId, id));
            });
        }
        finally
        {
            _busy = false;
        }

    }

    private async Task RecordApprovalAsync(DocumentApprovalRequest request)
    {
        if (_busy || _selectionDirty || _result?.Value is not { GenerationId: { } id })
            return;
        var selection = _loaded;
        if (await dialogService.ShowMessageBoxAsync("Save document assessment?",
            "This records an audited readiness decision bound to the saved chunk review. Unresolved gates remain blocked. No embedding or publication occurs.",
            yesText: "Save assessment", cancelText: "Cancel") != true || selection != _loaded)
            return;
        _busy = true;
        try
        {
            await RunAsync(async token =>
            {
                var result = await apiClient.RecordDocumentApprovalAsync(AppId, CollectionId, DocumentId, JobId, id, request, token);
                _approvalFieldErrors = result.FieldErrors;
                token.ThrowIfCancellationRequested();
                _writeResult = result;
                if (result is { IsSuccess: true, Value: { } saved })
                {
                    _result = result;
                    _projection = DocumentReviewProjectionView.Read(saved.Projection);
                    _approvalDirty = false;
                }
            });
        }
        finally
        {
            _busy = false;
        }
    }
}
