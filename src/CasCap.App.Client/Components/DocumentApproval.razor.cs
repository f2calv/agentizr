using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace CasCap.Client.Components;

/// <summary>Edits explicit document readiness decisions without granting indexing eligibility.</summary>
public partial class DocumentApproval(IDialogService dialogService)
{
    private DocumentReviewGenerationResponse? _loaded;
    private DocumentApprovalDraft? _draft;
    private EditContext? _context;
    private bool _dirty;
    private ValidationMessageStore? _messages;
    private IReadOnlyDictionary<string, string[]>? _loadedErrors;

    /// <summary>Gets the exact snapshot and its saved selection and assessment.</summary>
    [Parameter, EditorRequired]
    public DocumentReviewGenerationResponse Review { get; set; } = null!;

    /// <summary>Gets whether another form or mutation prevents editing.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Gets whether another dirty form prevents assessment edits without preventing navigation.</summary>
    [Parameter]
    public bool EditingBlocked { get; set; }

    /// <summary>Gets server field validation errors, retaining entered values after rejection.</summary>
    [Parameter]
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; set; }

    private bool Disabled => Busy || EditingBlocked;

    /// <summary>Gets the explicit save callback; no automatic mutation retries occur.</summary>
    [Parameter]
    public EventCallback<DocumentApprovalRequest> SaveAssessment { get; set; }

    /// <summary>Gets dirty-state notifications used to protect refresh and other forms.</summary>
    [Parameter]
    public EventCallback<bool> DirtyChanged { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_loaded, Review))
        {
            _loaded = Review;
            Reset();
        }
        if (!ReferenceEquals(_loadedErrors, FieldErrors))
        {
            _loadedErrors = FieldErrors;
            _messages!.Clear();
            if (FieldErrors is not null)
                foreach (var (key, errors) in FieldErrors)
                {
                    var name = key[(key.LastIndexOf('.') + 1)..];
                    var field = name.Length == 0 ? string.Empty : char.ToUpperInvariant(name[0]) + name[1..];
                    _messages.Add(new FieldIdentifier(_draft!, field), errors);
                }
            _context!.NotifyValidationStateChanged();
        }
    }

    private void Reset()
    {
        _draft = new(Review);
        _context = new(_draft);
        _messages = new(_context);
        _dirty = false;
    }

    private async Task ChangedAsync()
    {
        _dirty = true;
        _messages!.Clear();
        _context!.NotifyValidationStateChanged();
        await DirtyChanged.InvokeAsync(true);
    }

    private Task SaveAsync() => SaveAssessment.InvokeAsync(_draft!.Request());

    private async Task DiscardAsync()
    {
        Reset();
        await DirtyChanged.InvokeAsync(false);
    }

    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        if (Busy)
        {
            context.PreventNavigation();
            return;
        }
        if (_dirty && await dialogService.ShowMessageBoxAsync("Discard unsaved assessment?",
            "The document assessment has not been saved.", yesText: "Discard edits", cancelText: "Keep editing") != true)
            context.PreventNavigation();
    }

}
