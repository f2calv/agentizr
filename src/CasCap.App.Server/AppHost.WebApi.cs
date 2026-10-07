using CasCap.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;

namespace CasCap;

/// <summary>Registers the HTTP API baseline.</summary>
public static partial class AppHost
{
    private static bool AddWebApi(WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddControllers();
        builder.Services.AddValidation();

        var authenticationConfig = builder.Configuration
            .GetSection(TenantAuthenticationConfig.ConfigurationSectionName)
            .Get<TenantAuthenticationConfig>() ?? new TenantAuthenticationConfig();
        builder.Services.AddOptionsWithValidateOnStart<TenantAuthenticationConfig>()
            .BindConfiguration(TenantAuthenticationConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

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
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AgentRuntimePolicies.DefinitionRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasScope(context.User, AgentRuntimeScopes.DefinitionRead));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionPublish, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasScope(context.User, AgentRuntimeScopes.DefinitionPublish));
                policy.RequireAssertion(context => HasActorIdentifier(context.User));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionActivate, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasScope(context.User, AgentRuntimeScopes.DefinitionActivate));
                policy.RequireAssertion(context => HasActorIdentifier(context.User));
            });
        });
        return true;
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope) =>
        principal.FindAll("scope")
            .Concat(principal.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

    private static bool HasActorIdentifier(ClaimsPrincipal principal) =>
        principal.Claims.Any(claim => claim.Type is "sub" or "client_id" or "azp"
            or ClaimTypes.NameIdentifier or ClaimTypes.Name);
}
