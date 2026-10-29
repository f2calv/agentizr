#if DEBUG
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CasCap.IntegrationTests;

/// <summary>Shares the real review-management boundary across SQLite and native PostgreSQL fixtures.</summary>
internal sealed class DocumentReviewWebApplicationFactory(
    IDbContextFactory<AgentRuntimeDbContext>? database,
    IReadOnlyList<string>? apps = null)
    : AdministrationSignInWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AgentRuntimeConfig>(options =>
            {
                foreach (var app in apps ?? ["tenant-a"])
                    options.Tenants[app] = options.Tenants["default"];
            });
            services.PostConfigure<AdministrationConfig>(options =>
            {
                foreach (var app in apps ?? ["tenant-a"])
                    options.AppAdministrators[app] = [DefaultAppAdministrator];
            });
            services.RemoveAll<IDbContextFactory<AgentRuntimeDbContext>>();
            services.RemoveAll<DocumentPreparationMarkdownStore>();
            if (database is not null)
            {
                services.AddSingleton(database);
                services.AddScoped<DocumentPreparationMarkdownStore>();
            }
            services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, MachineHandler>("Bearer", _ => { });
        });
    }

    private sealed class MachineHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer synthetic")
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("oid", DefaultAppAdministrator)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
#endif
