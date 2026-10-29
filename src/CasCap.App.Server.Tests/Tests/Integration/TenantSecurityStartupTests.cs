using CasCap.Constants;
using CasCap.Tests.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace CasCap.IntegrationTests;

/// <summary>Verifies production startup fails closed without tenant security dependencies.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TenantSecurityStartupTests
{
    [Fact]
    public void ProductionWithoutTenantAuthentication_IsRejected()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("JWT tenant authentication is required", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentWithTenantAuthentication_AnonymousExecutionIsRejected()
    {
        using var factory = CreateAuthenticatedFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/agents/assistant/runs",
            new RunAgentRequest { SessionId = "session", Input = "hello" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentWithTenantAuthentication_AnonymousDefinitionReadIsRejected()
    {
        using var factory = CreateAuthenticatedFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/agents/assistant/definitions",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/", true)]
    [InlineData("GET", "/apps/test", true)]
    [InlineData("GET", "/api/v1/unknown", false)]
    [InlineData("GET", "/admin/api/unknown", false)]
    [InlineData("POST", "/", false)]
    public async Task FrontendWithoutAdministration_ExplainsTheConfigurationGap(string method, string path, bool expectPage)
    {
        using var factory = CreateAuthenticatedFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(expectPage, response.Content.Headers.ContentType?.MediaType == "text/html");
        if (expectPage)
        {
            Assert.Contains("CasCap:AdministrationConfig:Enabled", body, StringComparison.Ordinal);
            Assert.Contains("TenantAuthenticationConfig is also disabled", body, StringComparison.Ordinal);
            Assert.Contains("https://localhost/signin-oidc", body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(body);
        }
    }

    [Fact]
    public void DevelopmentWithTenantAuthentication_ControllerPoliciesRemainSeparated()
    {
        using var factory = CreateAuthenticatedFactory("Development");
        using var scope = factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider
            .GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToArray();
        var sessionEndpoints = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "AdministrationSession")
            .ToArray();
        var protectedEndpoints = endpoints.Except(sessionEndpoints).ToArray();
        var expectedDefinitionPolicies = new Dictionary<string, string>
        {
            ["PublishAgentDefinitionV1"] = AgentRuntimePolicies.DefinitionPublish,
            ["GetAgentDefinitionHistoryV1"] = AgentRuntimePolicies.DefinitionRead,
            ["GetActiveAgentDefinitionV1"] = AgentRuntimePolicies.DefinitionRead,
            ["GetAgentDefinitionVersionV1"] = AgentRuntimePolicies.DefinitionRead,
            ["EvaluateAgentDefinitionV1"] = AgentRuntimePolicies.DefinitionActivate,
            ["ActivateAgentDefinitionV1"] = AgentRuntimePolicies.DefinitionActivate,
            ["GetAgentDefinitionActivationHistoryV1"] = AgentRuntimePolicies.DefinitionRead,
            ["DeactivateAgentDefinitionV1"] = AgentRuntimePolicies.DefinitionActivate,
        };

        Assert.Equal(19, protectedEndpoints.Count(endpoint => !AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata)));
#if DEBUG
        Assert.Equal(17, protectedEndpoints.Count(endpoint => AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata)));
#else
        Assert.Equal(11, protectedEndpoints.Count(endpoint => AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata)));
#endif
        // Session description, sign-in and sign-out must be reachable before an administrator signs in.
        Assert.Equal(3, sessionEndpoints.Length);
        Assert.All(sessionEndpoints, endpoint => Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        Assert.All(protectedEndpoints, endpoint =>
        {
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            Assert.NotNull(name);
            var expectedPolicy = AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata)
                ? AdministrationPolicies.AppAdministrator
                : expectedDefinitionPolicies.GetValueOrDefault(name, AgentRuntimePolicies.Execute);
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(metadata => metadata.Policy).OfType<string>();
            var documentAdministration = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName is
                "AdministrationDocuments" or "AdministrationDocumentReviews" or "AdministrationDocumentSynchronization";
            string[] expectedPolicies = documentAdministration
                ? [expectedPolicy, AdministrationPolicies.AuthenticatedDocumentReviewer] : [expectedPolicy];
            Assert.Equal(expectedPolicies.Order(StringComparer.Ordinal), policies.Order(StringComparer.Ordinal));
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });
    }

    [Fact]
    public void DevelopmentWithoutTenantCallerMappings_IsRejected()
    {
        using var factory = new TenantSecurityWebApplicationFactory("Development", includeTenantMappings: false);

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("Tenant caller mappings", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWithoutPostgreSql_IsRejected()
    {
        using var factory = CreateAuthenticatedFactory("Production");

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("PostgreSQL Agent Runtime definitions are required", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWithPostgreSql_DoesNotRequireRedis()
    {
        using var factory = new TenantSecurityWebApplicationFactory("Production", includeTenantMappings: true, includePostgres: true);
        using var scope = factory.Services.CreateScope();
        Assert.IsType<PostgresAgentStateStore>(scope.ServiceProvider.GetRequiredService<IAgentSessionLock>());
        Assert.IsType<PostgresAgentSessionStore>(scope.ServiceProvider.GetRequiredService<IAgentSessionStore>());
    }

    private static WebApplicationFactory<Program> CreateAuthenticatedFactory(string environment) =>
        new TenantSecurityWebApplicationFactory(environment, includeTenantMappings: true);

    private sealed class TenantSecurityWebApplicationFactory(
        string environment,
        bool includeTenantMappings,
        bool includePostgres = false) : WebApplicationFactory<Program>
    {
        private readonly IReadOnlyDictionary<string, string?> _startupEnvironment =
            new Dictionary<string, string?>
            {
                ["CasCap__DatabaseConfig__ConnectionString"] = includePostgres
                    ? PersistenceTestSettings.PostgreSql
                    : null,
                ["CasCap__TenantAuthenticationConfig__Enabled"] = bool.TrueString,
                ["CasCap__TenantAuthenticationConfig__Authority"] = "https://identity.example.com",
                ["CasCap__TenantAuthenticationConfig__Audience"] = "agentizr",
                ["CasCap__TenantAuthenticationConfig__TenantCallers__test__0"] = includeTenantMappings
                    ? "test-caller"
                    : null,
            };

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var previous = _startupEnvironment.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);

            foreach (var (key, value) in _startupEnvironment)
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

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(environment);
    }
}
