#if DEBUG
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Rejects unsafe or unbounded preparation profiles before admission can authorize paid processing.</summary>
[Trait("Category", "HybridPreparation")]
public sealed class DocumentPreparationPlanTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("version")]
    [InlineData("tools")]
    [InlineData("stateful")]
    [InlineData("api-key")]
    [InlineData("endpoint")]
    [InlineData("deployment")]
    [InlineData("schema")]
    [InlineData("output")]
    [InlineData("time")]
    [InlineData("pages")]
    [InlineData("management")]
    public void RequiresExactApprovedProfile(string invalid)
    {
        var plan = DocumentHybridPreparationTestData.Plan;
        var definition = plan.Definition;
        var settings = plan.Settings;
        switch (invalid)
        {
            case "version":
                definition = definition with { Version = "wrong" };
                break;
            case "tools":
                definition = definition with { Agent = definition.Agent with { IncludeRuntimeContext = true } };
                break;
            case "stateful":
                definition = definition with { Agent = definition.Agent with { Stateless = false } };
                break;
            case "api-key":
                definition = definition with { Provider = definition.Provider with { ApiKey = "synthetic-key" } };
                break;
            case "endpoint":
                definition = definition with { Provider = definition.Provider with { Endpoint = new Uri("https://example.com/v1/") } };
                break;
            case "deployment":
                settings = settings with { DeploymentResourceId = settings.DeploymentResourceId + "/extra" };
                break;
            case "schema":
                definition = definition with
                {
                    Agent = definition.Agent with { JsonOutput = definition.Agent.JsonOutput! with { Schema = JsonSerializer.SerializeToElement(new { type = "object" }) } },
                };
                break;
            case "output":
                settings = settings with { MaximumOutputTokens = 8193 };
                break;
            case "time":
                settings = settings with { RequestTimeoutSeconds = 0 };
                break;
            case "pages":
                settings = settings with { MaximumPages = 0 };
                break;
            case "management":
                settings = settings with { ManagementEndpoint = "https://example.com/" };
                break;
        }
        var exception = Record.Exception(() => DocumentPreparationPlanService.Validate(definition, settings));
        if (invalid == "valid")
            Assert.Null(exception);
        else
            Assert.NotNull(exception);
    }
}
#endif
