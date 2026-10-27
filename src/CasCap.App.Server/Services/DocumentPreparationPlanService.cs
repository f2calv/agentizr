using CasCap.Entities;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Freezes an explicitly selected active definition without reading another tenant's configuration or credentials.</summary>
internal static class DocumentPreparationPlanService
{
    /// <summary>Pins the admission plan inside the caller's inbox transaction; null never opts an existing job into paid preparation.</summary>
    public static async Task<string?> PinAsync(
        AgentRuntimeDbContext db,
        DocumentIngestionNotification notification,
        CancellationToken cancellationToken
    )
    {
        if (notification.AgentPreparation is not { } settings)
            return null;
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        var snapshot = await db.ActiveAgentDefinitions
            .Where(item => item.TenantId == notification.TenantId && item.AgentName == settings.AgentName
                && item.Snapshot!.DefinitionVersion == settings.DefinitionVersion)
            .Select(item => item.Snapshot!)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The selected preparation definition is unavailable or inactive.");
        var definition = Deserialize(snapshot);
        Validate(definition, settings);
        return JsonSerializer.Serialize(new DocumentPreparationPlan
        {
            TenantId = notification.TenantId,
            Definition = definition,
            DefinitionSha256 = DocumentPreparationIdentity.Hash(snapshot.DefinitionJson),
            Settings = settings,
        }, JsonSerializerOptions.Web);
    }

    /// <summary>Checks the effective no-tools, stateless, bounded Azure Responses profile before any paid operation.</summary>
    public static void Validate(AgentDefinition definition, DocumentAgentPreparationConfig settings)
    {
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        if (definition.Name != settings.AgentName || definition.Version != settings.DefinitionVersion
            || !definition.Agent.Enabled || !definition.Agent.Stateless || definition.Agent.IncludeRuntimeContext
            || definition.Agent.Tools.Length != 0 || definition.Agent.Prompts.Length != 0
            || !string.IsNullOrEmpty(definition.Agent.InstructionsSource) || string.IsNullOrWhiteSpace(definition.Agent.Instructions)
            || definition.Agent.JsonOutput is null || definition.Agent.MaximumOutputTokens is not > 0
            || settings.MaximumOutputTokens > definition.Agent.MaximumOutputTokens
            || definition.Provider.ApiKey is not null || !definition.Provider.UseResponsesApi
            || definition.Provider.Type != AgentType.AzureOpenAI || string.IsNullOrWhiteSpace(definition.Agent.Provider)
            || definition.Provider.Endpoint is not { IsAbsoluteUri: true } endpoint
            || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 || endpoint.AbsolutePath != "/"
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || Uri.UnescapeDataString(settings.DeploymentResourceId.Split('/')[^1]) != definition.Provider.ModelName)
            throw new InvalidOperationException("The selected preparation definition does not satisfy the approved stateless Azure Responses contract.");
        var expectedSchema = DocumentPreparationAgentTemplate.Create().Definition.GetProperty("agent")
            .GetProperty("jsonOutput").GetProperty("schema");
        if (!JsonElement.DeepEquals(expectedSchema, definition.Agent.JsonOutput.Schema))
            throw new InvalidOperationException("The preparation agent must use the supported physical-page transcription schema.");
    }

    private static AgentDefinition Deserialize(AgentDefinitionSnapshotEntity snapshot) =>
        snapshot.SchemaVersion == 1
            ? JsonSerializer.Deserialize<AgentDefinition>(snapshot.DefinitionJson, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("The selected preparation definition is empty.")
            : throw new InvalidOperationException("The selected preparation definition schema is unsupported.");
}
