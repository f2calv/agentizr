using Azure.Core;

namespace CasCap.Tests.Unit;

/// <summary>Tests tenant-scoped provider credential resolution.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class ConfigurationProviderCredentialStoreTests
{
    private static readonly TokenCredential Credential = new StaticTokenCredential();

    [Theory]
    [InlineData("tenant-a", "AzureOpenAi", true)]
    [InlineData("tenant-a", "EdgeGpu", false)]
    [InlineData("tenant-b", "AzureOpenAi", false)]
    [InlineData("unknown", "AzureOpenAi", false)]
    public void GetTokenCredential_OnlyForOptedInTenantProvider(string tenantId, string providerName, bool expected)
    {
        var config = Options.Create(new AgentRuntimeConfig
        {
            Tenants = new Dictionary<string, TenantAgentConfig>
            {
                ["tenant-a"] = new() { EntraIdProviders = ["AzureOpenAi"] },
                ["tenant-b"] = new(),
            },
        });
        var store = new ConfigurationProviderCredentialStore(
            config, new StaticTenantContext(tenantId), new RuntimeEntraCredential(() => Credential));

        Assert.Equal(expected, store.GetTokenCredential(providerName) is not null);
    }

    [Fact]
    public void GetTokenCredential_NullWhenRuntimeHasNoIdentity()
    {
        var config = Options.Create(new AgentRuntimeConfig
        {
            Tenants = new Dictionary<string, TenantAgentConfig>
            {
                ["tenant-a"] = new() { EntraIdProviders = ["AzureOpenAi"] },
            },
        });
        var store = new ConfigurationProviderCredentialStore(
            config, new StaticTenantContext("tenant-a"), new RuntimeEntraCredential(() => null));

        Assert.Null(store.GetTokenCredential("AzureOpenAi"));
    }

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
