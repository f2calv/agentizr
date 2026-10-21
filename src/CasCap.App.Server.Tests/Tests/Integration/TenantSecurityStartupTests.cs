using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
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

    [Fact]
    public void DevelopmentWithTenantAuthentication_DefinitionPublishDoesNotRequireExecutePermission()
    {
        using var factory = CreateAuthenticatedFactory("Development");
        using var scope = factory.Services.CreateScope();
        var endpoint = scope.ServiceProvider
            .GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText
                == "/api/v1/agents/{agentName}/definitions"
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") is true);

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .OfType<string>()
            .ToArray();

        Assert.Contains(AgentRuntimePolicies.DefinitionPublish, policies);
        Assert.DoesNotContain(AgentRuntimePolicies.Execute, policies);
    }

    [Fact]
    public void DevelopmentWithoutTenantCallerMappings_IsRejected()
    {
        using var factory = new TenantSecurityWebApplicationFactory("Development", includeTenantMappings: false);

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("Tenant caller mappings", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWithoutRedis_IsRejected()
    {
        using var factory = CreateAuthenticatedFactory("Production");

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("Redis-backed Agent Runtime state is required", exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateAuthenticatedFactory(string environment) =>
        new TenantSecurityWebApplicationFactory(environment, includeTenantMappings: true);

    private sealed class TenantSecurityWebApplicationFactory(
        string environment,
        bool includeTenantMappings) : WebApplicationFactory<Program>
    {
        private readonly IReadOnlyDictionary<string, string?> _startupEnvironment =
            new Dictionary<string, string?>
            {
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
