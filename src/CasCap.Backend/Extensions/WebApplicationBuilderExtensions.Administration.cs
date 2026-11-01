using CasCap.Client.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace CasCap.Extensions;

/// <summary>Registers human administration: shared Data Protection keys, antiforgery and OIDC cookie sign-in.</summary>
public static partial class WebApplicationBuilderExtensions
{
    private const string DataProtectionApplicationName = "agentizr";

    /// <summary>Registers shared keys, antiforgery and optional administration authentication.</summary>
    /// <param name="builder">Application builder.</param>
    /// <returns>Whether administration authentication is enabled.</returns>
    public static bool AddAgentRuntimeAdministration(this WebApplicationBuilder builder)
    {
        // Every replica and compatible release must share keys, or cookies and antiforgery tokens fail on the next pod.
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        var databaseConfig = builder.Configuration
            .GetSection(DatabaseConfig.ConfigurationSectionName)
            .Get<DatabaseConfig>() ?? new DatabaseConfig();
        // TODO: Protect persisted keys at rest (for example ProtectKeysWithCertificate) once a deployment
        // certificate source is chosen; until then the key XML in PostgreSQL is readable by database users.
        if (!string.IsNullOrWhiteSpace(databaseConfig.ConnectionString))
            dataProtection.PersistKeysToDbContext<AgentRuntimeDbContext>();

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = AdministrationRoutes.AntiforgeryHeaderName;
            options.Cookie.Name = "agentizr-antiforgery";
        });
        builder.Services.AddScoped(services => new AdministrationSessionService(
            services.GetRequiredService<IOptions<AdministrationConfig>>(),
            services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>(),
            services.GetRequiredService<AdministrationSetupService>()));

        var administrationConfig = builder.Configuration
            .GetSection(AdministrationConfig.ConfigurationSectionName)
            .Get<AdministrationConfig>() ?? new AdministrationConfig();
        if (!administrationConfig.Enabled)
            return false;

        builder.Services.AddAuthentication()
            .AddCookie(AdministrationAuthenticationSchemes.Cookie, options =>
            {
                options.Cookie.Name = "agentizr-admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(administrationConfig.SessionLifetimeMinutes);
                options.SlidingExpiration = true;
                // Sign-in starts only from the explicit login route; nothing else redirects to the identity provider.
                options.Events.OnRedirectToLogin = context => WriteStatusAsync(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => WriteStatusAsync(context, StatusCodes.Status403Forbidden);
            })
            .AddOpenIdConnect(AdministrationAuthenticationSchemes.OpenIdConnect, options =>
            {
                options.SignInScheme = AdministrationAuthenticationSchemes.Cookie;
                options.SignOutScheme = AdministrationAuthenticationSchemes.Cookie;
                options.Authority = administrationConfig.Authority;
                options.ClientId = administrationConfig.ClientId;
                options.ClientSecret = administrationConfig.ClientSecret;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.CallbackPath = administrationConfig.CallbackPath;
                options.SignedOutCallbackPath = administrationConfig.SignedOutCallbackPath;
                options.RequireHttpsMetadata = administrationConfig.HttpsMetadataRequired;
                options.TokenValidationParameters.NameClaimType = "name";
            });
        return true;
    }

    private static Task WriteStatusAsync(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
