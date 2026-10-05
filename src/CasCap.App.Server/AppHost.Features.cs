using CasCap.Common.Extensions;
using CasCap.Common.Models;

namespace CasCap;

/// <summary>Registers application-level host services.</summary>
public static partial class AppHost
{
    private static void AddFeatures(
        WebApplicationBuilder builder,
        GitMetadata gitMetadata,
        bool tenantAuthenticationEnabled)
    {
        builder.Services.AddSingleton(gitMetadata);
        builder.Services.AddOptionsWithValidateOnStart<AgentRuntimeConfig>()
            .BindConfiguration(AgentRuntimeConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var cachingConfig = builder.Configuration
            .GetSection(CachingConfig.ConfigurationSectionName)
            .Get<CachingConfig>() ?? new CachingConfig();
        var redisEnabled = cachingConfig.RemoteCache.IsEnabled
            && !string.IsNullOrWhiteSpace(cachingConfig.RemoteCacheConnectionString);
        if (redisEnabled)
            builder.Services.AddCasCapCaching(builder.Configuration);
        if (!redisEnabled && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Redis-backed Agent Runtime state is required outside Development.");

        if (tenantAuthenticationEnabled)
            builder.Services.AddScoped<ITenantContext, AuthenticatedTenantContext>();
        else
            builder.Services.AddScoped<ITenantContext, ConfiguredTenantContext>();

        builder.Services.AddScoped<IAgentDefinitionStore, ConfigurationAgentDefinitionStore>();
        builder.Services.AddScoped<IProviderCredentialStore, ConfigurationProviderCredentialStore>();
        if (redisEnabled)
        {
            builder.Services.AddScoped<IAgentSessionStore, RedisAgentSessionStore>();
            builder.Services.AddScoped<IAgentOverrideStore, RedisAgentOverrideStore>();
        }
        else
        {
            builder.Services.AddSingleton<AgentRuntimeState>();
            builder.Services.AddScoped<IAgentSessionStore, InMemoryAgentSessionStore>();
            builder.Services.AddScoped<IAgentOverrideStore, InMemoryAgentOverrideStore>();
        }
        builder.Services.AddHttpClient(nameof(CommonAgentExecutor));
        builder.Services.AddScoped<IAgentExecutor, CommonAgentExecutor>();
        builder.Services.AddScoped<AgentExecutionCoordinator>();
    }
}
