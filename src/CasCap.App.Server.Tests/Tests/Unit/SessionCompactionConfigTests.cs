using System.ComponentModel.DataAnnotations;

namespace CasCap.Tests.Unit;

/// <summary>Tests per-provider session compaction resolution and validation.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class SessionCompactionConfigTests
{
    private static AgentRuntimeConfig CreateConfig() => new()
    {
        SessionCompaction = new Dictionary<string, SessionCompactionConfig>
        {
            ["EdgeGpu"] = new() { TargetMessageCount = 4, TriggerMessageCount = 10 },
        },
        Tenants = new Dictionary<string, TenantAgentConfig>
        {
            ["tenant-a"] = new()
            {
                SessionCompaction = new Dictionary<string, SessionCompactionConfig>
                {
                    ["EdgeGpu"] = new() { TargetMessageCount = 6, TriggerMessageCount = 8 },
                },
            },
            ["tenant-b"] = new(),
        },
    };

    [Theory]
    [InlineData("tenant-a", "EdgeGpu", 6, 8)]
    [InlineData("tenant-b", "EdgeGpu", 4, 10)]
    [InlineData("tenant-b", "AzureOpenAi", 20, 20)]
    [InlineData("unknown", "EdgeGpu", 4, 10)]
    public void GetSessionCompaction_TenantOverridesRuntimeThenDefaults(
        string tenantId, string providerName, int expectedTarget, int expectedTrigger)
    {
        var policy = CreateConfig().GetSessionCompaction(tenantId, providerName);

        Assert.Equal(expectedTarget, policy.TargetMessageCount);
        Assert.Equal(expectedTrigger, policy.TriggerMessageCount);
    }

    [Fact]
    public void Validate_TriggerBelowTarget_ReportsEveryPolicyPath()
    {
        var config = new AgentRuntimeConfig
        {
            SessionCompaction = new Dictionary<string, SessionCompactionConfig>
            {
                ["EdgeGpu"] = new() { TargetMessageCount = 10, TriggerMessageCount = 4 },
            },
            Tenants = new Dictionary<string, TenantAgentConfig>
            {
                ["tenant-a"] = new()
                {
                    SessionCompaction = new Dictionary<string, SessionCompactionConfig>
                    {
                        ["AzureOpenAi"] = new() { TargetMessageCount = 0 },
                    },
                },
            },
        };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains("SessionCompaction:EdgeGpu"));
        Assert.Contains(results, r => r.MemberNames.Contains("Tenants:tenant-a:SessionCompaction:AzureOpenAi"));
    }

    [Fact]
    public void Validate_ValidPolicies_Pass()
    {
        var config = CreateConfig();

        Assert.True(Validator.TryValidateObject(config, new ValidationContext(config), [], validateAllProperties: true));
    }
}
