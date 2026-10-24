using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Shows an agent's immutable versions and activation audit, and activates, rolls back or deactivates it.</summary>
/// <remarks>Every change sends the active version this page displayed, so a concurrent decision returns a conflict.</remarks>
public partial class AgentDetails(
    AdministrationApiClient apiClient,
    IDialogService dialogService,
    ISnackbar snackbar) : CancellableComponentBase
{
    private (string AppId, string AgentName)? _loadedAgent;
    private string? _loadedVersion;
    private ApiResult<IReadOnlyList<AgentDefinitionHistoryItemResponse>>? _history;
    private ApiResult<IReadOnlyList<AgentDefinitionActivationResponse>>? _activations;
    private ApiResult<AgentDefinitionSnapshotResponse>? _selected;
    private ProviderCatalogueResponse? _catalogue;
    private bool _busy;

    /// <summary>Gets or sets the app selected by the route.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent selected by the route.</summary>
    [Parameter]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Gets or sets the version selected in the query string; defaults to the active or newest version.</summary>
    [SupplyParameterFromQuery(Name = "version")]
    public string? Version { get; set; }

    private string? ActiveVersion => _history?.Value?.FirstOrDefault(item => item.IsActive)?.DefinitionVersion;

    private string? SelectedVersion => Version ?? ActiveVersion ?? _history?.Value?.FirstOrDefault()?.DefinitionVersion;

    private bool CanChange => !_busy && _catalogue?.PublicationSupported == true;

    private string AgentPath => Agents.AgentPath(AppId, AgentName);

    private List<BreadcrumbItem> Breadcrumbs =>
    [
        new("Apps", href: "/"),
        new(AppId, href: Apps.AppPath(AppId)),
        new(AgentName, href: null, disabled: true),
    ];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedAgent != (AppId, AgentName))
        {
            _loadedAgent = (AppId, AgentName);
            await ReloadAsync();
        }
        else if (_loadedVersion != SelectedVersion)
        {
            await LoadSelectedAsync();
        }
    }

    private string VersionPath(string version) => $"{AgentPath}?version={Uri.EscapeDataString(version)}";

    private string EditPath(string version) => $"{AgentPath}/edit?from={Uri.EscapeDataString(version)}";

    private Task ReloadAsync() => RunAsync(
        async cancellationToken =>
        {
            _history = null;
            _selected = null;
            var history = apiClient.GetHistoryAsync(AppId, AgentName, cancellationToken);
            var activations = apiClient.GetActivationsAsync(AppId, AgentName, cancellationToken);
            var catalogue = apiClient.GetProvidersAsync(AppId, cancellationToken);
            _history = await history;
            _activations = await activations;
            _catalogue = (await catalogue).Value;
            await LoadSelectedCoreAsync(cancellationToken);
        },
        renew: true);

    private Task LoadSelectedAsync() => RunAsync(LoadSelectedCoreAsync);

    private async Task LoadSelectedCoreAsync(CancellationToken cancellationToken)
    {
        _loadedVersion = SelectedVersion;
        _selected = null;
        if (_loadedVersion is { } version)
            _selected = await apiClient.GetVersionAsync(AppId, AgentName, version, cancellationToken);
    }

    private async Task ActivateAsync(string version)
    {
        var activeVersion = ActiveVersion;
        var message = activeVersion is null
            ? $"Version {version} will serve all new runs of {AgentName}."
            : $"Version {version} will replace {activeVersion} for all new runs of {AgentName}. Replicas may keep the previous version for a few seconds.";
        var confirmed = await ConfirmAsync($"Activate {version}", message, "Activate", Color.Primary, showEvaluation: true);
        if (confirmed is null)
            return;
        await ChangeAsync(
            cancellationToken => apiClient.ActivateAsync(
                AppId,
                AgentName,
                version,
                new AdministrationActivationRequest
                {
                    ChangeReason = confirmed.Reason,
                    ExpectedActiveVersion = activeVersion,
                    Evaluation = confirmed.Evaluation,
                },
                cancellationToken),
            $"Activated {version}.");
    }

    private async Task DeactivateAsync()
    {
        if (ActiveVersion is not { } activeVersion)
            return;
        var confirmed = await ConfirmAsync(
            $"Deactivate {AgentName}",
            $"{AgentName} will stop serving runs until a version is activated again. Agents that delegate to it must be deactivated first.",
            "Deactivate",
            Color.Error,
            showEvaluation: false);
        if (confirmed is null)
            return;
        await ChangeAsync(
            cancellationToken => apiClient.DeactivateAsync(
                AppId,
                AgentName,
                new AdministrationDeactivationRequest { ChangeReason = confirmed.Reason, ExpectedActiveVersion = activeVersion },
                cancellationToken),
            $"Deactivated {AgentName}.");
    }

    private async Task<ChangeReasonDialogResult?> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        Color confirmColor,
        bool showEvaluation)
    {
        var dialog = await dialogService.ShowAsync<ChangeReasonDialog>(title, new DialogParameters<ChangeReasonDialog>
        {
            { dialogComponent => dialogComponent.Message, message },
            { dialogComponent => dialogComponent.ConfirmText, confirmText },
            { dialogComponent => dialogComponent.ConfirmColor, confirmColor },
            { dialogComponent => dialogComponent.ShowEvaluation, showEvaluation },
            { dialogComponent => dialogComponent.EvaluationRequired, _catalogue?.EvaluationRequiredForActivation == true },
        });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: ChangeReasonDialogResult confirmed } ? confirmed : null;
    }

    private async Task ChangeAsync(Func<CancellationToken, Task<ApiResult<bool>>> change, string successMessage)
    {
        _busy = true;
        try
        {
            await RunAsync(async cancellationToken =>
            {
                var outcome = await change(cancellationToken);
                var (severity, message) = outcome.Status switch
                {
                    ApiStatus.Success => (Severity.Success, successMessage),
                    ApiStatus.Conflict => (Severity.Warning, "Another administrator changed the active version first. Review the refreshed page and try again."),
                    ApiStatus.ValidationFailed => (Severity.Error, string.Join(" ", outcome.Errors ?? ["The change was rejected."])),
                    ApiStatus.Unauthorized => (Severity.Error, "Your session has ended. Sign in again."),
                    ApiStatus.Forbidden => (Severity.Error, "You are not assigned to administer this app."),
                    ApiStatus.NotFound => (Severity.Error, "The version no longer exists."),
                    ApiStatus.Unavailable => (Severity.Error, "The server could not apply the change. Its outcome is unknown; refresh before retrying."),
                    _ => (Severity.Error, "The change failed. Refresh before retrying."),
                };
                snackbar.Add(message, severity);
            });
        }
        finally
        {
            _busy = false;
        }
        await ReloadAsync();
    }
}
