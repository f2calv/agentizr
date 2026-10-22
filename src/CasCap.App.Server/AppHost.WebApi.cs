using CasCap.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CasCap;

/// <summary>Registers the HTTP API baseline.</summary>
public static partial class AppHost
{
    private static bool AddWebApi(WebApplicationBuilder builder)
    {
        builder.Services.AddAgentRuntimeApi();

        var authenticationConfig = builder.Configuration
            .GetSection(TenantAuthenticationConfig.ConfigurationSectionName)
            .Get<TenantAuthenticationConfig>() ?? new TenantAuthenticationConfig();
        builder.Services.AddOptionsWithValidateOnStart<TenantAuthenticationConfig>()
            .BindConfiguration(TenantAuthenticationConfig.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .Validate(
                config => !config.Enabled
                    || AgentRuntimeAuthorization.HasValidTenantCallers(config.TenantCallers),
                "Tenant caller mappings must contain unique, non-empty caller identifiers for non-empty tenants.");

        if (!authenticationConfig.Enabled)
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("JWT tenant authentication is required outside Development.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(authenticationConfig.Authority)
            || string.IsNullOrWhiteSpace(authenticationConfig.Audience))
            throw new InvalidOperationException("JWT tenant authentication requires Authority and Audience.");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authenticationConfig.Authority;
                options.Audience = authenticationConfig.Audience;
                options.RequireHttpsMetadata = authenticationConfig.HttpsMetadataRequired;
                options.MapInboundClaims = false;
            });
        return true;
    }

}
