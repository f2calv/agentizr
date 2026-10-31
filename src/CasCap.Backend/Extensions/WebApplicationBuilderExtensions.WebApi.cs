using CasCap.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CasCap.Extensions;

/// <summary>Registers the HTTP API baseline.</summary>
public static partial class WebApplicationBuilderExtensions
{
    /// <summary>Registers the runtime API and validates optional tenant authentication.</summary>
    /// <param name="builder">Application builder.</param>
    /// <returns>Whether tenant authentication is enabled.</returns>
    public static bool AddAgentRuntimeWebApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddAgentRuntimeApi();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOptionsWithValidateOnStart<AdministrationConfig>()
            .BindConfiguration(AdministrationConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

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
