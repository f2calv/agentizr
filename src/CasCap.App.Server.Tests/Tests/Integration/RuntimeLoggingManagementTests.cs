#if DEBUG
using CasCap.AgentRuntime.Contracts.RuntimeConfiguration;
using CasCap.Client.Constants;
using CasCap.Common.Configuration.Management.Contracts.Constants;
using CasCap.Common.Configuration.Management.Contracts.Models;
using CasCap.Common.Configuration.Runtime.Abstractions;
using CasCap.Common.Configuration.Runtime.Models;
using CasCap.Constants;
using CasCap.IntegrationTests;
using CasCap.RuntimeConfiguration.Models;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CasCap.Tests.Integration;

/// <summary>Verifies the host's management wiring with isolated PostgreSQL and named browser authentication.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RuntimeLoggingManagementTests(RuntimeLoggingPostgreSqlFixture postgres) : IClassFixture<RuntimeLoggingPostgreSqlFixture>
{
    private const string Operator = "00000000-0000-0000-0000-00000000000a";
    private const string OtherAdministrator = "00000000-0000-0000-0000-00000000000b";
    private const string CookieHeader = "X-Test-Cookie";

    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData(null, true, HttpStatusCode.Unauthorized)]
    [InlineData(OtherAdministrator, false, HttpStatusCode.Forbidden)]
    [InlineData(Operator, false, HttpStatusCode.OK)]
    public async Task OnlyExplicitBrowserOperatorsCanRead(string? actor, bool bearer, HttpStatusCode expected)
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app, actor, bearer);
        using var bootstrap = await client.GetAsync(RuntimeLoggingRoutes.Destination, TestContext.Current.CancellationToken);
        Assert.Equal(expected, bootstrap.StatusCode);
        using var document = await client.GetAsync(DocumentPath(app), TestContext.Current.CancellationToken);
        Assert.Equal(expected, document.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True(document.Headers.CacheControl?.NoStore);
            var value = await document.Content.ReadFromJsonAsync<RuntimeConfigurationDocument<RuntimeLoggingControls>>(
                TestContext.Current.CancellationToken);
            Assert.NotNull(value);
            Assert.Equal(RuntimeLoggingLevel.Warning, value.Value.MinimumLevel);
        }
    }

    [Fact]
    public async Task PreviewAndPublishRequireAntiforgeryAndUseActualReader()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app, Operator, bearer: false);
        var path = DocumentPath(app);
        using var rejected = await client.PostAsJsonAsync(path + "/preview",
            new { Value = new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Debug } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var publisher = app.Services.GetRequiredService<IRuntimeConfigurationPublisher<RuntimeLoggingControls>>();
        var initial = await publisher.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(initial);
        using var tokenResponse = await client.GetAsync(path + "/antiforgery", TestContext.Current.CancellationToken);
        var token = await tokenResponse.Content.ReadFromJsonAsync<RuntimeConfigurationAntiforgeryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            tokenResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        client.DefaultRequestHeaders.Add(token.HeaderName, token.RequestToken);
        using var preview = await client.PostAsJsonAsync(path + "/preview",
            new { Value = new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Debug } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(initial.Revision, (await publisher.ReadAsync(TestContext.Current.CancellationToken))!.Revision);
        using var published = await client.PostAsJsonAsync(path + "/publish", new
        {
            Value = new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Debug },
            ExpectedRevision = initial.Revision,
            Reason = "Isolated host editor test",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var probe = app.Services.GetRequiredService<RuntimeLoggingProbe>();
        while (probe.ReadStatus().EffectiveMinimumLevel != nameof(RuntimeLoggingLevel.Debug))
            await Task.Delay(25, deadline.Token);
        Assert.Equal(initial.Revision + 1, (await publisher.ReadAsync(TestContext.Current.CancellationToken))!.Revision);
        using var wrongScope = await client.GetAsync(path.Replace("/logging", "/unknown", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, wrongScope.StatusCode);
    }

    private async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CasCap:RuntimeConfigurationConfig:Enabled"] = "true",
            ["CasCap:RuntimeConfigurationConfig:Application"] = $"fixture-{Guid.NewGuid():N}",
            ["CasCap:RuntimeConfigurationConfig:ConnectionString"] = postgres.ConnectionString,
            ["CasCap:RuntimeConfigurationConfig:RefreshIntervalMs"] = "100",
            ["CasCap:RuntimeConfigurationConfig:ManagementEnabled"] = "true",
            ["CasCap:RuntimeConfigurationConfig:ManagementOperators:0"] = Operator,
            ["CasCap:AdministrationConfig:Enabled"] = "true",
            ["Serilog:MinimumLevel:Default"] = "Information",
        });
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddOptions<CasCap.Models.AdministrationConfig>()
            .BindConfiguration(CasCap.Models.AdministrationConfig.ConfigurationSectionName);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Bearer", _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(AdministrationAuthenticationSchemes.Cookie, _ => { });
        CasCap.Extensions.WebApplicationBuilderExtensions.AddAgentRuntimeLogging(builder);
        CasCap.Extensions.WebApplicationBuilderExtensions.AddAgentRuntimeLoggingManagement(builder);
        builder.Services.AddControllers().AddApplicationPart(typeof(CasCap.Extensions.WebApplicationBuilderExtensions).Assembly);
        var app = builder.Build();
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<CasCap.Common.Configuration.PostgreSql.RuntimeConfigurationDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken))
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<IRuntimeConfigurationPublisher<RuntimeLoggingControls>>()
            .PublishAsync(new() { MinimumLevel = RuntimeLoggingLevel.Warning }, 0, "test-operator", "Initial isolated document",
                cancellationToken: TestContext.Current.CancellationToken);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static HttpClient CreateClient(WebApplication app, string? actor, bool bearer)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://example.com/");
        if (actor is not null)
            client.DefaultRequestHeaders.Add(CookieHeader, actor);
        if (bearer)
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test");
        return client;
    }

    private static string DocumentPath(WebApplication app)
    {
        var destination = app.Services.GetRequiredService<RuntimeLoggingManagementService>().Destination;
        return $"api/runtime-configuration/{destination.Application}/{destination.Environment}/{destination.Contract}";
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var actor = Scheme.Name == "Bearer"
                ? Request.Headers.ContainsKey("Authorization") ? Operator : null
                : Request.Headers[CookieHeader].FirstOrDefault();
            if (actor is null)
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", actor), new Claim("sub", "test-subject")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
#endif
