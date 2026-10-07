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
        var store = new ConfigurationMcpCredentialStore(config, new StaticTenantContext("tenant-a"));

        var authorization = await store.GetAuthorizationHeaderAsync(
            "tools",
            TestContext.Current.CancellationToken);
        var missing = await store.GetAuthorizationHeaderAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer tenant-a-token", authorization);
        Assert.Null(missing);
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}