using CasCap.Common.Extensions;
using CasCap.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace CasCap;

/// <summary>Registers application-level host services.</summary>
public static partial class AppHost
{
    private static void AddFeatures(
        WebApplicationBuilder builder,
        bool tenantAuthenticationEnabled)
    {
        builder.Services.AddOptionsWithValidateOnStart<AgentRuntimeConfig>()
            .BindConfiguration(AgentRuntimeConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        builder.Services.AddOptionsWithValidateOnStart<AgentDefinitionPolicyConfig>()
            .BindConfiguration(AgentDefinitionPolicyConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var cachingConfig = builder.Configuration
            .GetSection(CachingConfig.ConfigurationSectionName)
            .Get<CachingConfig>() ?? new CachingConfig();
        var redisEnabled = cachingConfig.RemoteCache.IsEnabled
            && !string.IsNullOrWhiteSpace(cachingConfig.RemoteCacheConnectionString);
        if (redisEnabled && cachingConfig.RemoteCache.SerializationType != SerializationType.Json)
            throw new InvalidOperationException("Agent Runtime Redis records require JSON serialization.");
        if (redisEnabled)
            builder.Services.AddCasCapCaching(builder.Configuration);
        if (!redisEnabled && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Redis-backed Agent Runtime state is required outside Development.");

        var databaseConfig = builder.Configuration
            .GetSection(AgentRuntimeDatabaseConfig.ConfigurationSectionName)
            .Get<AgentRuntimeDatabaseConfig>() ?? new AgentRuntimeDatabaseConfig();
        builder.Services.AddOptionsWithValidateOnStart<AgentRuntimeDatabaseConfig>()
            .BindConfiguration(AgentRuntimeDatabaseConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        var postgresEnabled = !string.IsNullOrWhiteSpace(databaseConfig.ConnectionString);
        if (!postgresEnabled && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("PostgreSQL Agent Runtime definitions are required outside Development.");

        if (tenantAuthenticationEnabled)
        {
            builder.Services.AddScoped<ITenantContext, AuthenticatedTenantContext>();
            builder.Services.AddScoped<IActorContext, AuthenticatedActorContext>();
        }
        else
        {
            builder.Services.AddScoped<ITenantContext, ConfiguredTenantContext>();
            builder.Services.AddScoped<IActorContext, ConfiguredActorContext>();
        }

        if (postgresEnabled)
        {
            builder.Services.AddDbContextFactory<AgentRuntimeDbContext>(options =>
                options.UseNpgsql(databaseConfig.ConnectionString));
            builder.Services.AddScoped<AgentRuntimeDatabaseMigrator>();
            builder.Services.AddScoped<PostgresAgentDefinitionStore>();
            if (redisEnabled)
            {
                builder.Services.AddScoped<CachedAgentDefinitionStore>();
                builder.Services.AddScoped<IAgentDefinitionStore>(services =>
                    services.GetRequiredService<CachedAgentDefinitionStore>());
                builder.Services.AddScoped<IAgentDefinitionAdministrationStore>(services =>
                    services.GetRequiredService<CachedAgentDefinitionStore>());
            }
            else
            {
                builder.Services.AddScoped<IAgentDefinitionStore>(services =>
                    services.GetRequiredService<PostgresAgentDefinitionStore>());
                builder.Services.AddScoped<IAgentDefinitionAdministrationStore>(services =>
                    services.GetRequiredService<PostgresAgentDefinitionStore>());
            }
        }
        else
        {
            builder.Services.AddScoped<ConfigurationAgentDefinitionStore>();
            builder.Services.AddScoped<IAgentDefinitionStore>(services =>
                services.GetRequiredService<ConfigurationAgentDefinitionStore>());
            builder.Services.AddScoped<IAgentDefinitionAdministrationStore>(services =>
                services.GetRequiredService<ConfigurationAgentDefinitionStore>());
        }
        builder.Services.AddScoped<IProviderCredentialStore, ConfigurationProviderCredentialStore>();
        builder.Services.AddScoped<IMcpCredentialStore, ConfigurationMcpCredentialStore>();
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
        builder.Services.AddSingleton<AgentRuntimeMetrics>();
        builder.Services.AddScoped<IAgentSessionCodec, CommonAgentSessionCodec>();
        builder.Services.AddScoped<AgentRuntimeBuiltInTools>();
        builder.Services.AddScoped<IAgentExecutor, CommonAgentExecutor>();
        builder.Services.AddScoped<AgentExecutionCoordinator>();
        builder.Services.AddScoped<AgentSessionControlService>();
        builder.Services.AddScoped<AgentDefinitionAdministrationService>();
    }
}
