using CasCap.Common.Extensions;
using CasCap.Common.Models;
using Microsoft.EntityFrameworkCore;

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
            builder.Services.AddScoped<ITenantContext, AuthenticatedTenantContext>();
        else
            builder.Services.AddScoped<ITenantContext, ConfiguredTenantContext>();

        if (postgresEnabled)
        {
            builder.Services.AddDbContextFactory<AgentRuntimeDbContext>(options =>
                options.UseNpgsql(databaseConfig.ConnectionString));
            builder.Services.AddScoped<PostgresAgentDefinitionStore>();
            if (redisEnabled)
                builder.Services.AddScoped<IAgentDefinitionStore, CachedAgentDefinitionStore>();
            else
                builder.Services.AddScoped<IAgentDefinitionStore>(services =>
                    services.GetRequiredService<PostgresAgentDefinitionStore>());
        }
        else
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
