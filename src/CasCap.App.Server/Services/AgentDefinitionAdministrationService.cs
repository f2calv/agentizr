using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Exceptions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasCap.Services;

/// <summary>Owns validation, attribution, publication and activation of tenant definitions.</summary>
public sealed class AgentDefinitionAdministrationService(
    IOptions<AgentDefinitionPolicyConfig> policyConfig,
    IActorContext actorContext,
    IAgentDefinitionAdministrationStore administrationStore)
{
    private const int MaximumDelegationDepth = 5;

    private static readonly JsonSerializerOptions DefinitionJsonOptions = new(JsonSerializerOptions.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Gets the active immutable snapshot.</summary>
    public ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken) =>
        administrationStore.GetActiveAsync(agentName, cancellationToken);

    /// <summary>Gets one immutable snapshot by version.</summary>
    public ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken) =>
        administrationStore.GetVersionAsync(agentName, definitionVersion, cancellationToken);

    /// <summary>Gets newest-first immutable snapshot history.</summary>
    public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        administrationStore.GetHistoryAsync(agentName, limit, cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken) =>
        administrationStore.GetActivationHistoryAsync(agentName, limit, cancellationToken);

    /// <summary>Publishes one validated immutable definition snapshot without activating it.</summary>
    public async ValueTask<AgentDefinitionPublishOutcome> PublishAsync(
        string agentName,
        PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentName) || agentName.Length > 200)
            return new AgentDefinitionPublishOutcome { ValidationError = "Agent name must contain 1 to 200 characters." };
        if (string.IsNullOrWhiteSpace(request.DefinitionVersion) || request.DefinitionVersion.Length > 100)
            return new AgentDefinitionPublishOutcome { ValidationError = "Definition version must contain 1 to 100 characters." };
        if (request.DefinitionVersion.Equals("active", StringComparison.OrdinalIgnoreCase)
            || request.DefinitionVersion.Equals("activations", StringComparison.OrdinalIgnoreCase))
            return new AgentDefinitionPublishOutcome { ValidationError = "Definition version is reserved by the HTTP API." };

        AgentDefinitionPayload? payload;
        try
        {
            payload = request.Definition.Deserialize<AgentDefinitionPayload>(DefinitionJsonOptions);
        }
        catch (JsonException)
        {
            return new AgentDefinitionPublishOutcome { ValidationError = "Definition is not a valid agent/provider document." };
        }

        var validationError = ValidatePayload(agentName, payload, request.SchemaVersion);
        if (validationError is not null)
            return new AgentDefinitionPublishOutcome { ValidationError = validationError };
        var validatedPayload = payload!;

        var definition = new AgentDefinition
        {
            Name = agentName,
            Version = request.DefinitionVersion,
            Agent = validatedPayload.Agent,
            Provider = validatedPayload.Provider with { ApiKey = null },
        };
        try
        {
            await administrationStore.PublishAsync(
                definition,
                request.SchemaVersion,
                actorContext.ActorId,
                request.ChangeReason,
                cancellationToken);
        }
        catch (AgentDefinitionVersionConflictException)
        {
            return new AgentDefinitionPublishOutcome { IsConflict = true };
        }
        catch (NotSupportedException)
        {
            return new AgentDefinitionPublishOutcome { IsSupported = false };
        }
        catch (ValidationException ex)
        {
            return new AgentDefinitionPublishOutcome { ValidationError = ex.ValidationResult?.ErrorMessage ?? ex.Message };
        }

        return new AgentDefinitionPublishOutcome
        {
            Snapshot = await administrationStore.GetVersionAsync(
                agentName,
                request.DefinitionVersion,
                cancellationToken),
        };
    }

    /// <summary>Activates an existing version and records the authenticated actor.</summary>
    public async ValueTask<AgentDefinitionActivationOutcome> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentName) || agentName.Length > 200
            || string.IsNullOrWhiteSpace(definitionVersion) || definitionVersion.Length > 100)
            return new AgentDefinitionActivationOutcome { ValidationError = "Agent name or definition version is invalid." };

        try
        {
            var snapshot = await administrationStore.GetVersionAsync(agentName, definitionVersion, cancellationToken);
            if (snapshot is null)
                return new AgentDefinitionActivationOutcome();
            var delegationError = await ValidateDelegationGraphAsync(
                snapshot.Definition,
                [],
                depth: 0,
                cancellationToken);
            if (delegationError is not null)
                return new AgentDefinitionActivationOutcome { ValidationError = delegationError };

            return new AgentDefinitionActivationOutcome
            {
                Activated = await administrationStore.ActivateAsync(
                    agentName,
                    definitionVersion,
                    actorContext.ActorId,
                    changeReason,
                    cancellationToken),
            };
        }
        catch (NotSupportedException)
        {
            return new AgentDefinitionActivationOutcome { IsSupported = false };
        }
    }

    private async ValueTask<string?> ValidateDelegationGraphAsync(
        AgentDefinition definition,
        HashSet<string> path,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > MaximumDelegationDepth)
            return $"Agent delegation cannot exceed {MaximumDelegationDepth} levels.";
        if (!path.Add(definition.Name))
            return $"Agent delegation contains a cycle at '{definition.Name}'.";

        foreach (var delegatedAgent in definition.Agent.Tools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.Agent))
            .Select(tool => tool.Agent!))
        {
            var delegatedSnapshot = await administrationStore.GetActiveAsync(delegatedAgent, cancellationToken);
            if (delegatedSnapshot is null)
                return $"Delegated agent '{delegatedAgent}' has no active definition.";
            var validationError = await ValidateDelegationGraphAsync(
                delegatedSnapshot.Definition,
                path,
                depth + 1,
                cancellationToken);
            if (validationError is not null)
                return validationError;
        }
        path.Remove(definition.Name);
        return null;
    }

    private string? ValidatePayload(string agentName, AgentDefinitionPayload? payload, int schemaVersion)
    {
        if (payload?.Agent is null || payload.Provider is null)
            return "Definition must contain non-null agent and provider objects.";
        if (!policyConfig.Value.SupportedSchemaVersions.Contains(schemaVersion))
            return $"Schema version {schemaVersion} is not supported.";
        if (!string.IsNullOrWhiteSpace(payload.Provider.ApiKey))
            return "Provider credentials are not permitted in definitions.";
        if (!string.IsNullOrWhiteSpace(payload.Agent.InstructionsSource))
            return "Definitions must use inline instructions; file or resource instruction sources are not permitted.";
        if (string.IsNullOrWhiteSpace(payload.Agent.Instructions))
            return "Definitions must provide inline instructions.";
        if (payload.Agent.Settings is not null)
            return "Arbitrary agent settings are not supported in definition schema version 1.";
        if (payload.Agent.Tools is null || payload.Agent.Prompts is null)
            return "Tool and prompt collections cannot be null.";
        if (payload.Agent.Prompts.Length > 0)
            return "Prompt sources are not supported in definition schema version 1.";

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
            payload.Agent,
            new ValidationContext(payload.Agent),
            validationResults,
            validateAllProperties: true))
            return validationResults[0].ErrorMessage ?? "Agent configuration is invalid.";
        validationResults.Clear();
        if (!Validator.TryValidateObject(
            payload.Provider,
            new ValidationContext(payload.Provider),
            validationResults,
            validateAllProperties: true))
            return validationResults[0].ErrorMessage ?? "Provider configuration is invalid.";

        if (payload.Provider.Type is not (AgentType.Ollama or AgentType.AzureOpenAI or AgentType.OpenAI))
            return $"Provider type {payload.Provider.Type} is not supported by the runtime.";
        if (payload.Provider.Endpoint is { } providerEndpoint)
        {
            if (!providerEndpoint.IsAbsoluteUri)
                return "Provider endpoint must be an absolute URI.";
            if (ValidateEndpoint(providerEndpoint, policyConfig.Value.AllowedProviderAuthorities) is { } providerError)
                return $"Provider endpoint {providerError}";
        }
        if (payload.Provider.Type is not AgentType.OpenAI && payload.Provider.Endpoint is null)
            return $"Provider type {payload.Provider.Type} requires an endpoint.";

        foreach (var tool in payload.Agent.Tools)
        {
            if (tool is null)
                return "Tool entries cannot be null.";
            validationResults.Clear();
            if (!Validator.TryValidateObject(tool, new ValidationContext(tool), validationResults, validateAllProperties: true))
                return validationResults[0].ErrorMessage ?? "Tool configuration is invalid.";
            if (tool.IncludeTools is null || tool.ExcludeTools is null)
                return "Tool include/exclude collections cannot be null.";
            if (tool.Service is { } service
                && !policyConfig.Value.AllowedServiceTools.Contains(service, StringComparer.Ordinal))
                return $"Service tool '{service}' is not permitted.";
            if (tool.Endpoint is { } endpoint)
            {
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
                    return "Remote MCP endpoint is not an absolute URI.";
                if (ValidateEndpoint(endpointUri, policyConfig.Value.AllowedMcpAuthorities) is { } endpointError)
                    return $"Remote MCP endpoint {endpointError}";
            }
            if (tool.Agent is { } delegatedAgent)
            {
                if (delegatedAgent.Equals(agentName, StringComparison.OrdinalIgnoreCase))
                    return "An agent cannot delegate directly to itself.";
                if (delegatedAgent.Length > 200)
                    return "Delegated agent names cannot exceed 200 characters.";
            }
        }
        return null;
    }

    private static string? ValidateEndpoint(Uri endpoint, IEnumerable<string> allowedAuthorities)
    {
        if (endpoint.Scheme is not ("http" or "https"))
            return "must use HTTP or HTTPS.";
        if (!string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            return "cannot contain user information, query parameters or fragments.";
        var authority = endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return allowedAuthorities.Contains(authority, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"authority '{authority}' is not permitted.";
    }
}
