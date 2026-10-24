using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace CasCap.Client.Pages;

/// <summary>Creates an agent or copies a version, then publishes the edits as a new inactive version.</summary>
/// <remarks>
/// Edits keep fields that have no dedicated control. Unsaved work is kept after errors and protected by a navigation
/// prompt, and the publish button is disabled while a request is in flight.
/// </remarks>
public partial class DefinitionEditor(
    AdministrationApiClient apiClient,
    NavigationManager navigationManager,
    IDialogService dialogService) : CancellableComponentBase
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
            if (string.IsNullOrEmpty(_form.ProviderName) && _catalogue.Providers.FirstOrDefault() is { } registration)
                ApplyRegistration(registration);
            _editContext = new EditContext(_form);
            _messages = new ValidationMessageStore(_editContext);
            _editContext.OnFieldChanged += (_, _) =>
            {
                _dirty = true;
                _serverErrors = null;
                _messages.Clear();
            };
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
