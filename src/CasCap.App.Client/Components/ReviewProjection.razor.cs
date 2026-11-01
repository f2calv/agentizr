using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using System.Text.Json;

namespace CasCap.Client.Components;

/// <summary>Displays contextual chunk candidates as escaped text with separate Markdown provenance.</summary>
public partial class ReviewProjection(IDialogService dialogService)
{
    private DocumentReviewProjectionView? _view;
    private DocumentChunkSelectionDraft? _draft;
    private JsonElement _loadedProjection;
    private DocumentChunkReviewResponse? _loadedReview;
    private string _filter = All;
    private const string All = "All";
    private const string Included = "Included";
    private const string Excluded = "Excluded";

    /// <summary>Gets or sets the server-owned versioned projection.</summary>
    [Parameter, EditorRequired]
    public JsonElement Projection { get; set; }

    /// <summary>Gets the latest persisted selection, if any.</summary>
    [Parameter]
    public DocumentChunkReviewResponse? Selection { get; set; }

    /// <summary>Gets whether this is an exact stored snapshot on which selections may be recorded.</summary>
    [Parameter]
    public bool CanSelect { get; set; }

    /// <summary>Gets whether a parent mutation is in progress.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Gets whether another dirty form prevents selection edits without preventing navigation.</summary>
    [Parameter]
    public bool EditingBlocked { get; set; }

    /// <summary>Gets the explicit save callback; selection never triggers automatic persistence.</summary>
    [Parameter]
    public EventCallback<IReadOnlyList<DocumentChunkExclusionRequest>> SaveSelection { get; set; }

    /// <summary>Gets the dirty-state notification used to prevent parent refresh from discarding edits.</summary>
    [Parameter]
    public EventCallback<bool> DirtyChanged { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (_loadedProjection.Equals(Projection) && ReferenceEquals(_loadedReview, Selection))
            return;
        _loadedProjection = Projection;
        _loadedReview = Selection;
        try
        {
            _view = DocumentReviewProjectionView.Read(Projection);
            if (Selection is { } selection && (selection.Revision < 1 || selection.Exclusions is null
                || selection.Exclusions.Any(item => item is null || !_view.Chunks.Any(chunk => chunk.Ordinal == item.Ordinal))
                || selection.Exclusions.Select(item => item.Ordinal).Distinct().Count() != selection.Exclusions.Count))
                throw new JsonException("The saved selection does not match the snapshot.");
            _draft = new(_view.Chunks.Select(chunk => chunk.Ordinal), Selection);
        }
        catch (JsonException)
        {
            _view = null;
            _draft = null;
        }
    }

    private bool Visible(DocumentReviewProjectionView.ChunkView chunk) =>
        _filter == All || (_draft!.IsExcluded(chunk.Ordinal) ? _filter == Excluded : _filter == Included);

    private async Task SetIncludedAsync(int ordinal, bool included)
    {
        _draft!.SetIncluded(ordinal, included);
        await DirtyChanged.InvokeAsync(_draft.IsDirty);
    }

    private async Task SetReasonAsync(int ordinal, string? reason)
    {
        _draft!.SetReason(ordinal, reason);
        await DirtyChanged.InvokeAsync(_draft.IsDirty);
    }

    private async Task DiscardAsync()
    {
        _draft = new(_view!.Chunks.Select(chunk => chunk.Ordinal), Selection);
        await DirtyChanged.InvokeAsync(false);
    }

    private Task SaveAsync() => SaveSelection.InvokeAsync(_draft!.Exclusions());

    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        if (Busy)
        {
            context.PreventNavigation();
            return;
        }
        if (_draft?.IsDirty != true)
            return;
        var confirmed = await dialogService.ShowMessageBoxAsync("Discard unsaved chunk decisions?",
            "These inclusion and exclusion edits have not been saved.", yesText: "Discard edits", cancelText: "Keep editing");
        if (confirmed != true)
            context.PreventNavigation();
    }
}
