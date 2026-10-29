using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace CasCap.Client.Components;

/// <summary>Displays contextual chunk candidates as escaped text with separate Markdown provenance.</summary>
public partial class ReviewProjection
{
    private DocumentReviewProjectionView? _view;

    /// <summary>Gets or sets the server-owned versioned projection.</summary>
    [Parameter, EditorRequired]
    public JsonElement Projection { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        try
        {
            _view = DocumentReviewProjectionView.Read(Projection);
        }
        catch (JsonException)
        {
            _view = null;
        }
    }
}
