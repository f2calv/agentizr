using CasCap.Common.Models;

namespace CasCap;

/// <summary>Registers application-level host services.</summary>
public static partial class AppHost
{
    private static void AddFeatures(
        WebApplicationBuilder builder,
        GitMetadata gitMetadata)
    {
        builder.Services.AddSingleton(gitMetadata);
        builder.Services.AddOptionsWithValidateOnStart<AgentRuntimeConfig>()
            .BindConfiguration(AgentRuntimeConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        builder.Services.AddSingleton<AgentRuntimeState>();
        builder.Services.AddScoped<ITenantContext, ConfiguredTenantContext>();
        builder.Services.AddScoped<IAgentDefinitionStore, ConfigurationAgentDefinitionStore>();
        builder.Services.AddScoped<IProviderCredentialStore, ConfigurationProviderCredentialStore>();
        builder.Services.AddScoped<IAgentSessionStore, InMemoryAgentSessionStore>();
        builder.Services.AddScoped<IAgentOverrideStore, InMemoryAgentOverrideStore>();
        builder.Services.AddHttpClient(nameof(CommonAgentExecutor));
        builder.Services.AddScoped<IAgentExecutor, CommonAgentExecutor>();
        builder.Services.AddScoped<AgentExecutionCoordinator>();
    }
}
