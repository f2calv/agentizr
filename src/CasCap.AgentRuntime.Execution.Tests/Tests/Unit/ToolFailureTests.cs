using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace CasCap.AgentRuntime.Execution.Tests.Unit;

/// <summary>
/// Verifies that tool exceptions and MCP <c>isError</c> results are flagged on <see cref="AgentRunResult.ToolCalls"/>
/// instead of passing to the model as ordinary output.
/// </summary>
[Trait("Category", "Tool Failures")]
public sealed class ToolFailureTests
{
    [Fact]
    public async Task RunAnalysisAsync_FlagsThrowingAndErrorResultTools()
    {
        var activities = new List<AgentRunActivity>();
        var scope = new AgentRunScope
        {
            OnActivity = (activity, _) =>
            {
                activities.Add(activity);
                return ValueTask.CompletedTask;
            },
        };
        var agent = BuildAgent(
            [
                AIFunctionFactory.Create(() => "ok", "healthy_tool"),
                AIFunctionFactory.Create(string () => throw new InvalidOperationException("boom"), "throwing_tool"),
                AIFunctionFactory.Create(() => JsonSerializer.SerializeToElement(new { isError = true }), "error_result_tool"),
            ],
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "healthy_tool", new Dictionary<string, object?>()),
                new FunctionCallContent("call-2", "throwing_tool", new Dictionary<string, object?>()),
                new FunctionCallContent("call-3", "error_result_tool", new Dictionary<string, object?>()),
            ])),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));

        var result = await agent.RunAnalysisAsync(
            new ProviderConfig { Type = AgentType.Ollama, ModelName = "test-model", Endpoint = new Uri("http://ollama.local:11434") },
            new AgentConfig { Provider = "test-provider", Name = "failure-probe", Description = "d", Prompt = "p", Instructions = "i" },
            AgentExtensions.BuildChatMessage("hi"),
            new ChatOptions(),
            cancellationToken: TestContext.Current.CancellationToken,
            scope: scope);

        Assert.Equal(
            [("healthy_tool", false), ("throwing_tool", true), ("error_result_tool", true)],
            result.ToolCalls.Select(toolCall => (toolCall.Name, toolCall.Failed)));
        Assert.Equal(2, result.ToolErrorCount);
        Assert.Equal(3, activities.Count(activity => activity.Kind == "tool-started"));
        Assert.Equal(3, activities.Count(activity => activity.Kind == "tool-completed"));
        var successfulResult = Assert.Single(activities, activity =>
            activity.Kind == "tool-completed" && activity.ToolName == "healthy_tool");
        Assert.Contains("ok", JsonSerializer.Serialize(successfulResult.Data), StringComparison.Ordinal);
        var failure = Assert.Single(activities, activity =>
            activity.Kind == "tool-completed" && activity.ToolName == "throwing_tool");
        Assert.Contains(nameof(InvalidOperationException), JsonSerializer.Serialize(failure.Data), StringComparison.Ordinal);
        Assert.DoesNotContain("boom", JsonSerializer.Serialize(failure.Data), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAnalysisAsync_RefusesToolCallsBeyondTheRunBudget()
    {
        var invocations = 0;
        var agent = BuildAgent(
            [AIFunctionFactory.Create(() => ++invocations, "counted_tool")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "counted_tool", new Dictionary<string, object?>()),
                new FunctionCallContent("call-2", "counted_tool", new Dictionary<string, object?>()),
            ])),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        var scope = new AgentRunScope { MaximumToolInvocations = 1 };

        var result = await agent.RunAnalysisAsync(
            new ProviderConfig { Type = AgentType.Ollama, ModelName = "test-model", Endpoint = new Uri("http://ollama.local:11434") },
            new AgentConfig { Provider = "test-provider", Name = "budget-probe", Description = "d", Prompt = "p", Instructions = "i" },
            AgentExtensions.BuildChatMessage("hi"),
            new ChatOptions(),
            cancellationToken: TestContext.Current.CancellationToken,
            scope: scope);

        Assert.Equal(1, invocations);
        Assert.Equal(2, scope.ToolInvocationCount);
        Assert.Equal([false, true], result.ToolCalls.Select(toolCall => toolCall.Failed));
        Assert.Equal("done", result.OutputText);
    }

    [Fact]
    public void ForSubAgent_SharesTheToolBudget()
    {
        var parent = new AgentRunScope
        {
            MaximumToolInvocations = 2,
            OnActivity = (_, _) => ValueTask.CompletedTask,
        };
        var child = parent.ForSubAgent();

        Assert.True(parent.TryConsumeToolInvocation());
        Assert.True(child.TryConsumeToolInvocation());
        Assert.False(parent.TryConsumeToolInvocation());
        Assert.Equal(3, child.ToolInvocationCount);
        Assert.Same(parent.OnActivity, child.OnActivity);
    }

    [Theory]
    [InlineData("{\"isError\":true,\"content\":[]}", true)]
    [InlineData("{\"isError\":false,\"content\":[]}", false)]
    [InlineData("{\"content\":[]}", false)]
    [InlineData("[]", false)]
    public void IsErrorToolResult_ReadsSerializedCallToolResults(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, AgentExtensions.IsErrorToolResult(document.RootElement.Clone()));
    }

    [Fact]
    public void IsErrorToolResult_ReadsCallToolResultsAndIgnoresText()
    {
        Assert.True(AgentExtensions.IsErrorToolResult(new CallToolResult { IsError = true, Content = [] }));
        Assert.False(AgentExtensions.IsErrorToolResult("isError"));
        Assert.False(AgentExtensions.IsErrorToolResult(null));
    }

    private static AIAgent BuildAgent(IList<AITool> tools, params ChatResponse[] responses)
    {
        var chatClient = new ScriptedChatClient(responses).AsBuilder().UseFunctionInvocation().Build();
        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "failure-probe",
            ChatOptions = new ChatOptions { Tools = tools },
        })
            .AsBuilder()
            .Use(AgentExtensions.CreateFunctionCallingMiddleware())
            .Build();
    }

    private sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
    {
        private int _index;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
