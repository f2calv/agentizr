using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Unit;

/// <summary>Tests tool composition rules applied before agent construction.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class CommonAgentExecutorTests
{
    [Fact]
    public void AddUniqueTools_DuplicateNamePreservesFirstTool()
    {
        var first = CreateTool("get_agents");
        var duplicate = CreateTool("get_agents");
        var unique = CreateTool("get_status");
        var tools = new List<AITool> { first };

        CommonAgentExecutor.AddUniqueTools(
            tools,
            [duplicate, unique],
            "test tools",
            NullLogger.Instance);

        Assert.Equal(2, tools.Count);
        Assert.Same(first, tools[0]);
        Assert.Same(unique, tools[1]);
    }

    [Fact]
    public void RemoveComposedIncludes_RuntimeBuiltInsSatisfyRemoteIncludes()
    {
        var source = new ToolSource
        {
            Endpoint = "https://example.com/mcp",
            IncludeTools = ["get_agents", "get_house_rooms", "get_providers"],
        };
        AITool[] builtIns = [CreateTool("get_agents"), CreateTool("get_providers")];

        var result = CommonAgentExecutor.RemoveComposedIncludes(source, builtIns);

        Assert.Equal(["get_house_rooms"], result.IncludeTools);
        Assert.Equal(3, source.IncludeTools.Length);
    }

    private static AIFunction CreateTool(string name) =>
        AIFunctionFactory.Create(
            (Func<string>)(() => name),
            new AIFunctionFactoryOptions { Name = name });
}
