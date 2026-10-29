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
}
