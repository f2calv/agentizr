using CasCap.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CasCap.IntegrationTests;

/// <summary>Runs the host with administrator sign-in enabled and a header-driven stand-in for the session cookie.</summary>
public sealed class AdministrationSignInWebApplicationFactory : AgentDefinitionAdministrationWebApplicationFactory
{
    /// <summary>Gets the request header naming the test administrator's <c>oid</c>.</summary>
    public const string AdministratorHeader = "X-Test-Administrator";

    /// <summary>Gets the administrator assigned to the default app.</summary>
    public const string DefaultAppAdministrator = "00000000-0000-0000-0000-00000000000a";

    /// <summary>Gets the administrator assigned only to the other app.</summary>
    public const string OtherAppAdministrator = "00000000-0000-0000-0000-00000000000b";

    /// <inheritdoc/>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // AppHost reads AdministrationConfig eagerly to register schemes, before test configuration is layered in.
        Dictionary<string, string?> startupEnvironment = new()
        {
            ["CasCap__AdministrationConfig__Enabled"] = bool.TrueString,
            ["CasCap__AdministrationConfig__Authority"] = "https://login.example.com/00000000-0000-0000-0000-000000000000/v2.0",
            ["CasCap__AdministrationConfig__ClientId"] = "00000000-0000-0000-0000-000000000000",
            ["CasCap__AdministrationConfig__ClientSecret"] = "test-client-secret",
            ["CasCap__AdministrationConfig__AppAdministrators__default__0"] = DefaultAppAdministrator,
            ["CasCap__AdministrationConfig__AppAdministrators__other__0"] = OtherAppAdministrator,
            // An assignment alone does not create an app; the runtime must define it.
            ["CasCap__AgentRuntimeConfig__Tenants__other__TimeZoneId"] = "UTC",
        };
        var previous = startupEnvironment.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        foreach (var (key, value) in startupEnvironment)
            Environment.SetEnvironmentVariable(key, value);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.Configure<AuthenticationOptions>(options =>
                options.Schemes.Single(scheme => scheme.Name == AdministrationAuthenticationSchemes.Cookie)
                    .HandlerType = typeof(TestAdministratorHandler)));
    }

    private sealed class TestAdministratorHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AdministratorHeader, out var administratorId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [
                    new Claim("oid", administratorId.ToString()),
                    new Claim("sub", $"subject-{administratorId}"),
                    new Claim("name", "Example Administrator"),
                ],
                Scheme.Name,
                "name",
                ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
