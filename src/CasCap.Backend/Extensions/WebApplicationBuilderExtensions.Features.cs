using CasCap.Common.Extensions;
using CasCap.Common.Models;

namespace CasCap.Extensions;

/// <summary>Registers application-level host services.</summary>
public static partial class WebApplicationBuilderExtensions
{
    /// <summary>Registers runtime execution, definition administration and shared prerequisites.</summary>
    /// <param name="builder">Application builder.</param>
    /// <param name="tenantAuthenticationEnabled">Whether caller identity is authenticated.</param>
    public static void AddAgentRuntimeFeatures(
        this WebApplicationBuilder builder,
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

        var postgresEnabled = builder.AddAgentRuntimeDatabase(!builder.Environment.IsDevelopment());
        AddDocumentReviews(builder, postgresEnabled);

        if (tenantAuthenticationEnabled)
        {
            builder.Services.AddScoped<AuthenticatedTenantContext>();
            builder.Services.AddScoped<ITenantContext>(services => new AdministrationTenantContext(
                services.GetRequiredService<IHttpContextAccessor>(),
                services.GetRequiredService<AuthenticatedTenantContext>()));
            builder.Services.AddScoped<IActorContext, AuthenticatedActorContext>();
        }
        else
        {
            builder.Services.AddScoped<ConfiguredTenantContext>();
            builder.Services.AddScoped<ITenantContext>(services => new AdministrationTenantContext(
                services.GetRequiredService<IHttpContextAccessor>(),
                services.GetRequiredService<ConfiguredTenantContext>()));
            // A signed-in administrator is still audited by identity when machine authentication is disabled.
            builder.Services.AddScoped<IActorContext>(services =>
                services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User.Identity?.IsAuthenticated is true
                    ? new AuthenticatedActorContext(services.GetRequiredService<IHttpContextAccessor>())
                    : new ConfiguredActorContext());
        }

        if (postgresEnabled)
        {
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
        // Unvalidated: only Basic MCP credentials without their own parameter read it.
        builder.Services.AddOptions<ApiAuthConfig>().BindConfiguration(ApiAuthConfig.ConfigurationSectionName);
        if (postgresEnabled)
        {
            builder.Services.AddScoped<PostgresAgentStateStore>();
            builder.Services.AddScoped<AgentRunHistoryService>();
            builder.Services.AddScoped<IAgentSessionLock>(services =>
                services.GetRequiredService<PostgresAgentStateStore>());
            builder.Services.AddScoped<IAgentSessionStore, PostgresAgentSessionStore>();
            builder.Services.AddScoped<IAgentOverrideStore, PostgresAgentOverrideStore>();
        }
        else
        {
            builder.Services.AddSingleton<AgentRuntimeState>();
            builder.Services.AddSingleton<InMemoryAgentSessionLock>();
            builder.Services.AddScoped<IAgentSessionLock, ConfiguredAgentSessionLock>();
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
        builder.Services.AddScoped<IEvaluationAgentFactory, EvaluationAgentFactory>();
        builder.Services.AddScoped<AgentDefinitionEvaluator>();
        builder.Services.AddScoped<AgentDefinitionAdministrationService>();
    }
}
