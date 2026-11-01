using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Creates or copies a version, publishes it and optionally activates it with a guarded, confirmed request.</summary>
/// <remarks>
/// Edits keep fields that have no dedicated control. Unsaved work is kept after errors and protected by a navigation
/// prompt, and the publish button is disabled while a request is in flight.
/// </remarks>
public partial class DefinitionEditor(
    AdministrationApiClient apiClient,
    NavigationManager navigationManager,
    IDialogService dialogService,
    ISnackbar snackbar) : CancellableComponentBase
{
    private (string AppId, string? AgentName, string? From)? _loaded;
    private ApiStatus? _loadStatus;
    private IReadOnlyList<string>? _loadErrors;
    private ProviderCatalogueResponse? _catalogue;
    private DefinitionDraft? _draft;
    private DefinitionForm? _form;
    private EditContext? _editContext;
    private ValidationMessageStore? _messages;
    private IReadOnlyList<string>? _serverErrors;
    private bool _dirty;
    private bool _publishing;
    private bool _activateAfterPublishing;
    private string? _expectedActiveVersion;

    /// <summary>Gets or sets the app selected by the route.</summary>
    [Parameter]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent selected by the route, or <see langword="null" /> for a new agent.</summary>
    [Parameter]
    public string? AgentName { get; set; }

    /// <summary>Gets or sets the version to copy, from the query string.</summary>
    [SupplyParameterFromQuery(Name = "from")]
    public string? From { get; set; }

    private bool IsNew => AgentName is null;

    private string Heading => IsNew ? "New agent" : $"New version of {AgentName}";

    private string CancelPath => IsNew ? Apps.AppPath(AppId) : Agents.AgentPath(AppId, AgentName!);

    private string EndpointHelp => _catalogue is { AllowedProviderAuthorities.Count: > 0 } catalogue
        ? $"Allowed: {string.Join(", ", catalogue.AllowedProviderAuthorities)}. Optional only for OpenAI."
        : "Optional only for OpenAI.";

    private IEnumerable<string> ProviderNames => (_catalogue?.Providers.Select(provider => provider.Name) ?? [])
        .Append(_form?.ProviderName ?? string.Empty)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal);

    private List<BreadcrumbItem> Breadcrumbs =>
    [
        new("Apps", href: "/"),
        new(AppId, href: Apps.AppPath(AppId)),
        .. IsNew ? Array.Empty<BreadcrumbItem>() : [new BreadcrumbItem(AgentName!, href: Agents.AgentPath(AppId, AgentName!))],
        new(IsNew ? "New agent" : "New version", href: null, disabled: true),
    ];

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_loaded == (AppId, AgentName, From))
            return;
        _loaded = (AppId, AgentName, From);
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(
        async cancellationToken =>
        {
            _loadStatus = null;
            _activateAfterPublishing = false;
            _expectedActiveVersion = null;
            var catalogue = await apiClient.GetProvidersAsync(AppId, cancellationToken);
            if (!catalogue.IsSuccess)
            {
                (_loadStatus, _loadErrors) = (catalogue.Status, catalogue.Errors);
                return;
            }
            _catalogue = catalogue.Value!;
            if (AgentName is not null && From is not null)
            {
                var source = await apiClient.GetVersionAsync(AppId, AgentName, From, cancellationToken);
                if (!source.IsSuccess)
                {
                    (_loadStatus, _loadErrors) = (source.Status, source.Errors);
                    return;
                }
                _draft = DefinitionDraft.FromDefinition(source.Value!.Definition);
            }
            else
            {
                _draft = DefinitionDraft.Empty();
            }
            _form = _draft.ToForm(AgentName ?? string.Empty);
            if (AgentName is not null)
            {
                var suggestion = await apiClient.GetSuggestedVersionAsync(AppId, AgentName, cancellationToken);
                if (!suggestion.IsSuccess)
                {
                    (_loadStatus, _loadErrors) = (suggestion.Status, suggestion.Errors);
                    return;
                }
                _form.DefinitionVersion = suggestion.Value!;
                var active = await apiClient.GetActiveAsync(AppId, AgentName, cancellationToken);
                if (!active.IsSuccess && active.Status != ApiStatus.NotFound)
                {
                    (_loadStatus, _loadErrors) = (active.Status, active.Errors);
                    return;
                }
                _expectedActiveVersion = active.Value?.DefinitionVersion;
            }
            else
            {
                _form.DefinitionVersion = DefinitionDraft.SuggestVersion([]);
            }
            if (string.IsNullOrEmpty(_form.ProviderName) && _catalogue.Providers.FirstOrDefault() is { } registration)
                ApplyRegistration(registration);
            InitializeEditContext();
            _loadStatus = ApiStatus.Success;
        },
        renew: true);

    private void ProviderChanged(string name)
    {
        if (_catalogue?.Providers.FirstOrDefault(provider => provider.Name == name) is { } registration)
            ApplyRegistration(registration);
        else
            _form!.ProviderName = name;
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => _form!.ProviderName));
    }

    private async Task UseTemplateAsync(BuiltInDefinitionTemplateResponse template)
    {
        if (_dirty && await dialogService.ShowMessageBoxAsync(
            "Replace draft?",
            "Using this template replaces your unpublished edits.",
            yesText: "Use template",
            cancelText: "Keep editing") != true)
            return;
        _draft = DefinitionDraft.FromDefinition(template.Definition);
        var version = _form!.DefinitionVersion;
        var provider = _form.ProviderName;
        _form = _draft.ToForm(template.AgentName);
        _form.DefinitionVersion = version;
        _form.ProviderName = provider;
        InitializeEditContext();
        _serverErrors = null;
        _activateAfterPublishing = false;
        _dirty = true;
    }

    private void InitializeEditContext()
    {
        _editContext = new EditContext(_form!);
        _messages = new ValidationMessageStore(_editContext);
        _editContext.OnFieldChanged += (_, _) =>
        {
            _dirty = true;
            _serverErrors = null;
            _messages.Clear();
        };
    }

    // A registration only seeds the version: its endpoint authority may need the provider's path appended.
    private void ApplyRegistration(ProviderRegistrationResponse registration)
    {
        _form!.ProviderName = registration.Name;
        _form.ProviderType = registration.Type;
        _form.Endpoint = registration.EndpointAuthority;
        _form.ModelName = registration.ModelName;
        _form.ReasoningEffort = registration.ReasoningEffort;
    }

    private async Task PublishAsync()
    {
        if (_form is null || _draft is null || _catalogue is null || _messages is null || _editContext is null || _publishing)
            return;
        if (!_draft.TryCreatePayload(_form, out var payload, out var payloadError))
        {
            _messages.Add(() => _form.ToolsJson, payloadError!);
            _editContext.NotifyValidationStateChanged();
            return;
        }

        _publishing = true;
        var agentName = (AgentName ?? _form.AgentName).Trim();
        var version = _form.DefinitionVersion.Trim();
        try
        {
            var activation = _activateAfterPublishing ? await ConfirmActivationAsync(version) : null;
            if (_activateAfterPublishing && activation is null)
                return;
            await RunAsync(async cancellationToken =>
            {
                var result = await apiClient.PublishAsync(
                    AppId,
                    agentName,
                    new PublishAgentDefinitionRequest
                    {
                        DefinitionVersion = version,
                        SchemaVersion = _catalogue.SupportedSchemaVersions.Max(),
                        Definition = payload,
                        ChangeReason = _form.ChangeReason.Trim(),
                    },
                    cancellationToken);
                switch (result.Status)
                {
                    case ApiStatus.Success:
                        if (activation is not null)
                            await ActivatePublishedAsync(agentName, version, activation, cancellationToken);
                        _dirty = false;
                        navigationManager.NavigateTo($"{Agents.AgentPath(AppId, agentName)}?version={Uri.EscapeDataString(version)}");
                        break;
                    case ApiStatus.Conflict:
                        _messages.Add(() => _form.DefinitionVersion, "This version already exists. Versions are immutable; choose another.");
                        _editContext.NotifyValidationStateChanged();
                        break;
                    case ApiStatus.ValidationFailed:
                        _serverErrors = result.Errors;
                        break;
                    case ApiStatus.Unauthorized:
                        _serverErrors = ["Your session has ended. Sign in again in another tab, then publish; your edits are kept."];
                        break;
                    case ApiStatus.Forbidden:
                        _serverErrors = ["You are not assigned to administer this app."];
                        break;
                    default:
                        _serverErrors = ["The server could not publish the version. Check the agent page before retrying."];
                        break;
                }
            });
        }
        finally
        {
            _publishing = false;
        }
    }

    private async Task<AdministrationActivationRequest?> ConfirmActivationAsync(string version)
    {
        var expectedActiveVersion = _expectedActiveVersion;
        var message = expectedActiveVersion is null
            ? $"Version {version} will be published and serve all new runs."
            : $"Version {version} will be published and replace {expectedActiveVersion} for all new runs.";
        var dialog = await dialogService.ShowAsync<ChangeReasonDialog>(
            $"Publish and activate {version}",
            new DialogParameters<ChangeReasonDialog>
            {
                { component => component.Message, message },
                { component => component.ConfirmText, "Publish and activate" },
                { component => component.InitialReason, _form!.ChangeReason.Trim() },
                { component => component.ShowEvaluation, true },
                { component => component.EvaluationRequired, _catalogue!.EvaluationRequiredForActivation },
            });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: ChangeReasonDialogResult confirmed }
            ? new AdministrationActivationRequest
            {
                ChangeReason = confirmed.Reason,
                ExpectedActiveVersion = expectedActiveVersion,
                Evaluation = confirmed.Evaluation,
            }
            : null;
    }

    private async Task ActivatePublishedAsync(
        string agentName,
        string version,
        AdministrationActivationRequest activation,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.ActivateAsync(AppId, agentName, version, activation, cancellationToken);
        var (severity, message) = result.Status switch
        {
            ApiStatus.Success => (Severity.Success, $"Published and activated {version}."),
            ApiStatus.Conflict => (Severity.Warning, "Published, but not activated: another administrator changed the active version. Review the agent page before activating."),
            ApiStatus.ValidationFailed => (Severity.Error, $"Published, but not activated: {string.Join(" ", result.Errors ?? ["Activation validation failed."])}"),
            ApiStatus.Unauthorized => (Severity.Error, "Published, but not activated: your session has ended. Sign in again."),
            ApiStatus.Forbidden => (Severity.Error, "Published, but not activated: you are not assigned to administer this app."),
            ApiStatus.NotFound => (Severity.Error, "Published, but activation could not find the version. Refresh the agent page."),
            _ => (Severity.Error, "Published, but the activation outcome is unknown. Refresh the agent page before retrying; do not republish."),
        };
        snackbar.Add(message, severity);
    }

    private async Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        if (!_dirty)
            return;
        var leave = await dialogService.ShowMessageBoxAsync(
            "Discard changes?",
            "This version has not been published. Leaving discards your edits.",
            yesText: "Discard",
            cancelText: "Keep editing");
        if (leave != true)
            context.PreventNavigation();
    }
}
