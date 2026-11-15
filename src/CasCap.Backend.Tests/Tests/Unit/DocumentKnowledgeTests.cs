using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Constants;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace CasCap.Tests.Unit;

/// <summary>Checks retrieval grants, ordered CEFR ceilings, request limits and tokenizer compatibility without inference.</summary>
[Trait("Category", "Indexing")]
public sealed class DocumentKnowledgeTests
{
    [Theory]
    [InlineData("A1", "A1", true)]
    [InlineData("A1", "A2", false)]
    [InlineData("B1", "A2", true)]
    [InlineData("B2", "C1", false)]
    [InlineData("C2", "C2", true)]
    [InlineData("unknown", "A1", false)]
    [InlineData("C2", "unknown", false)]
    public void Levels_AreOrderedAndFailClosed(string maximum, string level, bool allowed) =>
        Assert.Equal(allowed, DocumentKnowledgeStore.AllowsLevel(maximum, level));

    [Theory]
    [InlineData("valid", true)]
    [InlineData("disabled", true)]
    [InlineData("missing", false)]
    [InlineData("duplicate", false)]
    [InlineData("level", false)]
    [InlineData("tokens", false)]
    [InlineData("null", false)]
    [InlineData("realtime", true)]
    [InlineData("realtime-level", false)]
    [InlineData("realtime-duration", false)]
    public void Configuration_RequiresExplicitUniqueBoundedGrants(string variation, bool valid)
    {
        var binding = new DocumentKnowledgeBindingConfig
        {
            TenantId = "tenant-a",
            AgentName = "assistant",
            CollectionId = "collection-a",
            MaximumCefrLevel = variation == "level" ? "Z9" : variation == "realtime-level" ? "A2" : "A1",
            MaximumQueryTokens = variation == "tokens" ? 513 : 128,
            RealtimeEnabled = variation.StartsWith("realtime", StringComparison.Ordinal),
            MaximumRealtimeDurationMinutes = variation == "realtime-duration" ? 31 : 10,
        };
        var config = new DocumentKnowledgeConfig
        {
            Enabled = variation != "disabled",
            Bindings = variation switch
            {
                "missing" => [],
                "duplicate" => [binding, binding],
                "null" => [null!],
                _ => [binding],
            },
        };
        Assert.Equal(valid, Validator.TryValidateObject(config, new ValidationContext(config), [], true));
    }

    [Theory]
    [InlineData("", 3, null, false)]
    [InlineData("   ", 3, null, false)]
    [InlineData("example query", 0, null, false)]
    [InlineData("example query", 9, null, false)]
    [InlineData("example query", 3, "A3", false)]
    [InlineData("example query", 8, "B1", true)]
    public void Requests_BoundTextCountAndOptionalLevel(string query, int results, string? level, bool valid)
    {
        var request = new SearchKnowledgeRequest { Query = query, MaximumResults = results, MaximumCefrLevel = level };
        Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public void QueryTokenizer_ReusesDocumentProfileAndRejectsDrift()
    {
        var profile = DocumentIndexingTestData.Config().Profiles[0];
        var chunking = new DocumentChunkingService();
        Assert.InRange(chunking.CountQueryTokens("synthetic short query", profile), 1, 128);
        Assert.Throws<InvalidDataException>(() => chunking.CountQueryTokens("example", profile with { TokenizerVersion = "incompatible" }));
        Assert.Throws<InvalidDataException>(() => chunking.CountQueryTokens("example", profile with { TokenizerEncoding = "incompatible" }));
    }

    [Theory]
    [InlineData(AgentRuntimePermissions.Execute, false)]
    [InlineData(AgentRuntimePermissions.Realtime, false)]
    [InlineData(AgentRuntimePermissions.KnowledgeSearch, true)]
    public void SearchPermission_IsSeparateFromExecutionAndRealtime(string permission, bool allowed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scp", permission)], "synthetic"));
        Assert.Equal(allowed, AgentRuntimeAuthorization.HasPermission(principal, AgentRuntimePermissions.KnowledgeSearch));
    }
}
