using CasCap.Common.Abstractions;

namespace CasCap.Tests.Unit;

/// <summary>Tests tenant-scoped remote MCP credential resolution.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class ConfigurationMcpCredentialStoreTests
{
    [Fact]
    public async Task GetAuthorizationHeaderAsync_ResolvesCurrentTenantOnly()
    {
        var config = Options.Create(new AgentRuntimeConfig
        {
            Tenants = new Dictionary<string, TenantAgentConfig>
            {
                ["tenant-a"] = new()
                {
                    McpCredentials = new Dictionary<string, McpCredentialConfig>
                    {
                        ["tools"] = new() { Scheme = "Bearer", Parameter = "tenant-a-token" },
                    },
                },
                ["tenant-b"] = new()
                {
                    McpCredentials = new Dictionary<string, McpCredentialConfig>
                    {
                        ["tools"] = new() { Scheme = "Bearer", Parameter = "tenant-b-token" },
                    },
                },
            },
        });
        var store = new ConfigurationMcpCredentialStore(
            config,
            Options.Create(new ApiAuthConfig { Username = "unused", Password = "unused" }),
            new StaticTenantContext("tenant-a"));

        var authorization = await store.GetAuthorizationHeaderAsync(
            "tools",
            TestContext.Current.CancellationToken);
        var missing = await store.GetAuthorizationHeaderAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer tenant-a-token", authorization);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetAuthorizationHeaderAsync_BasicWithoutParameterUsesSharedApiCredentials()
    {
        var config = Options.Create(new AgentRuntimeConfig
        {
            Tenants = new Dictionary<string, TenantAgentConfig>
            {
                ["tenant-a"] = new()
                {
                    McpCredentials = new Dictionary<string, McpCredentialConfig>
                    {
                        ["application-api"] = new() { Scheme = "Basic" },
                        ["bearer-without-parameter"] = new() { Scheme = "Bearer" },
                    },
                },
            },
        });
        var store = new ConfigurationMcpCredentialStore(
            config,
            Options.Create(new ApiAuthConfig { Username = "user", Password = "pass" }),
            new StaticTenantContext("tenant-a"));

        var basic = await store.GetAuthorizationHeaderAsync(
            "application-api",
            TestContext.Current.CancellationToken);
        var bearer = await store.GetAuthorizationHeaderAsync(
            "bearer-without-parameter",
            TestContext.Current.CancellationToken);

        Assert.Equal("Basic dXNlcjpwYXNz", basic);
        Assert.Null(bearer);
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
