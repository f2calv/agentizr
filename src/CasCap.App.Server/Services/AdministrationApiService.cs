using CasCap.Client.Constants;
using CasCap.Client.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasCap.Services;

/// <summary>Maps administrator discovery and guarded activation to browser-facing DTOs.</summary>
public sealed class AdministrationApiService
{
    private static readonly JsonSerializerOptions BrowserDefinitionJsonOptions = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IOptions<AgentDefinitionPolicyConfig> _policyConfig;
    private readonly IOptions<AgentRuntimeConfig> _runtimeConfig;
    private readonly IOptions<DatabaseConfig> _databaseConfig;
    private readonly AdministrationAccessService _accessSvc;
    private readonly AgentDefinitionAdministrationService _administrationSvc;

    /// <summary>Creates the DTO service without exposing internal administration dependencies.</summary>
    internal AdministrationApiService(
        IOptions<AgentDefinitionPolicyConfig> policyConfig,
        IOptions<AgentRuntimeConfig> runtimeConfig,
        IOptions<DatabaseConfig> databaseConfig,
        AdministrationAccessService accessSvc,
        AgentDefinitionAdministrationService administrationSvc)
    {
        _policyConfig = policyConfig;
        _runtimeConfig = runtimeConfig;
        _databaseConfig = databaseConfig;
        _accessSvc = accessSvc;
        _administrationSvc = administrationSvc;
        Definitions = new AgentDefinitionApiService(administrationSvc, BrowserDefinitionJsonOptions);
    }

    /// <summary>Gets definition reads, publication and evaluation, with enum names in snapshot definitions for the browser.</summary>
    public AgentDefinitionApiService Definitions { get; }

    /// <summary>Lists the apps the administrator may manage, with caller applications and agent counts.</summary>
    public async Task<IReadOnlyList<AppSummaryResponse>> GetAppsAsync(
        ClaimsPrincipal administrator,
        CancellationToken cancellationToken)
    {
        var appIds = _accessSvc.GetAdministeredApps(administrator);
        var agentCounts = await _administrationSvc.CountAgentsAsync(appIds, cancellationToken);
        return [.. appIds.Select(appId => new AppSummaryResponse
        {
            AppId = appId,
            CallerApplications = _accessSvc.GetCallerApplications(appId),
            AgentCount = agentCounts.GetValueOrDefault(appId),
        })];
    }

    /// <summary>Gets one page of the selected app's agents, including inactive and never-activated agents.</summary>
    public async Task<AgentSummaryPageResponse> GetAgentsAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _administrationSvc.GetAgentsAsync(search, (page - 1) * pageSize, pageSize, cancellationToken);
        return new AgentSummaryPageResponse
        {
            Items = [.. result.Items.Select(item => new AgentSummaryResponse
            {
                AgentName = item.AgentName,
                ActiveVersion = item.ActiveDefinition?.Version,
                ActiveProviderType = item.ActiveDefinition?.Provider.Type.ToString(),
                ActiveModelName = item.ActiveDefinition?.Provider.ModelName,
                LatestVersion = item.LatestVersion,
                LatestPublishedAtUtc = item.LatestPublishedAtUtc,
                LatestPublishedBy = item.LatestPublishedBy,
                VersionCount = item.VersionCount,
            })],
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Gets the app's secret-free provider registrations and the definition policy.</summary>
    public ProviderCatalogueResponse GetProviderCatalogue(string appId)
    {
        var policy = _policyConfig.Value;
        var tenant = _runtimeConfig.Value.Tenants.GetValueOrDefault(appId);
        return new ProviderCatalogueResponse
        {
            Providers = tenant is null
                ? []
                : [.. tenant.Providers
                    .OrderBy(provider => provider.Key, StringComparer.Ordinal)
                    .Select(provider => MapProvider(provider.Key, provider.Value, tenant, policy))],
            AllowedProviderAuthorities = policy.AllowedProviderAuthorities,
            AllowedMcpAuthorities = policy.AllowedMcpAuthorities,
            AllowedServiceTools = policy.AllowedServiceTools,
            SupportedSchemaVersions = policy.SupportedSchemaVersions,
            EvaluationRequiredForActivation = policy.RequireEvaluationBeforeActivation,
            PublicationSupported = !string.IsNullOrWhiteSpace(_databaseConfig.Value.ConnectionString),
        };
    }

    /// <summary>Activates or rolls back to a version only if the administrator's observed active version is current.</summary>
    public ValueTask<AgentDefinitionActivationOutcome> ActivateAsync(
        string agentName,
        string definitionVersion,
        AdministrationActivationRequest request,
        CancellationToken cancellationToken) =>
        _administrationSvc.ActivateAsync(
            agentName,
            definitionVersion,
            request.ChangeReason,
            request.Evaluation,
            cancellationToken,
            new ExpectedActiveDefinition(request.ExpectedActiveVersion));

    /// <summary>Deactivates the active definition only if it is still the administrator's observed version.</summary>
    public ValueTask<AgentDefinitionDeactivationOutcome> DeactivateAsync(
        string agentName,
        AdministrationDeactivationRequest request,
        CancellationToken cancellationToken) =>
        _administrationSvc.DeactivateAsync(
            agentName,
            request.ChangeReason,
            cancellationToken,
            new ExpectedActiveDefinition(request.ExpectedActiveVersion));

    private static ProviderRegistrationResponse MapProvider(
        string name,
        ProviderConfig provider,
        TenantAgentConfig tenant,
        AgentDefinitionPolicyConfig policy)
    {
        var authority = provider.Endpoint is { IsAbsoluteUri: true } endpoint
            ? endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/')
            : null;
        return new ProviderRegistrationResponse
        {
            Name = name,
            Type = provider.Type.ToString(),
            EndpointAuthority = authority,
            ModelName = provider.ModelName,
            ReasoningEffort = provider.ReasoningEffort?.ToString(),
            CredentialSource = tenant.EntraIdProviders.Contains(name) ? ProviderCredentialSources.EntraId
                : string.IsNullOrWhiteSpace(provider.ApiKey) ? ProviderCredentialSources.None
                : ProviderCredentialSources.ApiKey,
            IsEndpointAllowed = authority is null
                ? provider.Type is AgentType.OpenAI
                : policy.AllowedProviderAuthorities.Contains(authority, StringComparer.OrdinalIgnoreCase),
        };
    }
}
