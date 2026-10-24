using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Text.Json;

namespace CasCap.Client.Components;

/// <summary>Confirms an activation, rollback or deactivation with an audited reason and optional fixture evaluation.</summary>
public partial class ChangeReasonDialog : ComponentBase
{
    private const string EvaluationExample =
        """Example: {"scenarios":[{"id":"greeting","question":"Say hello.","answer":{"containsAny":["hello"]}}]}""";

    private string? _reason;
    private string? _evaluationJson;
    private string? _error;

    /// <summary>Gets or sets the dialog instance that receives the result.</summary>
    [CascadingParameter]
    public IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>Gets or sets the explanation of what confirming will change.</summary>
    [Parameter, EditorRequired]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the confirm button text.</summary>
    [Parameter]
    public string ConfirmText { get; set; } = "Confirm";

    /// <summary>Gets or sets the confirm button colour, such as an error colour for removing live traffic.</summary>
    [Parameter]
    public Color ConfirmColor { get; set; } = Color.Primary;

    /// <summary>Gets or sets whether the administrator may supply fixture scenarios that must pass first.</summary>
    [Parameter]
    public bool ShowEvaluation { get; set; }

    /// <summary>Gets or sets whether the server policy refuses activation without a passing evaluation.</summary>
    [Parameter]
    public bool EvaluationRequired { get; set; }

    private void Cancel() => MudDialog.Cancel();

    private void Confirm()
    {
        _error = null;
        if (string.IsNullOrWhiteSpace(_reason))
        {
            _error = "Enter a reason.";
            return;
        }
        EvaluateAgentDefinitionRequest? evaluation = null;
        if (!string.IsNullOrWhiteSpace(_evaluationJson))
        {
            try
            {
                evaluation = JsonSerializer.Deserialize<EvaluateAgentDefinitionRequest>(_evaluationJson, JsonSerializerOptions.Web);
            }
            catch (JsonException ex)
            {
                _error = $"The evaluation is not valid JSON: {ex.Message}";
                return;
            }
        }
        if (EvaluationRequired && evaluation is null)
        {
            _error = "This app requires a passing evaluation before activation.";
            return;
        }
        MudDialog.Close(DialogResult.Ok(new ChangeReasonDialogResult(_reason.Trim(), evaluation)));
    }
}
