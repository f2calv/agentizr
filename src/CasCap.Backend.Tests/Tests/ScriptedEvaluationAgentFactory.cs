using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CasCap.TestData;

/// <summary>
/// Creates evaluation agents backed by a scripted chat client, so evaluations run without a model provider.
/// Each run receives a fresh script, and function invocation runs through the evaluator's sandboxed tools.
/// </summary>
internal sealed class ScriptedEvaluationAgentFactory(params Func<ChatResponse>[] responses) : IEvaluationAgentFactory
{
    /// <summary>Gets the tool names offered to the most recently created agent.</summary>
    public IReadOnlyList<string> OfferedTools { get; private set; } = [];

    /// <inheritdoc/>
    public (AIAgent Agent, string Instructions) Create(ProviderConfig provider, AgentConfig agent, List<AITool> tools)
    {
        OfferedTools = tools.Select(tool => tool.Name).ToArray();
        var chatClient = new ScriptedChatClient(responses).AsBuilder().UseFunctionInvocation().Build();
        var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = agent.Name,
            ChatOptions = new ChatOptions { Tools = tools },
        });
        return (aiAgent, agent.Instructions ?? string.Empty);
    }

    /// <summary>Creates a model turn that calls one tool with the supplied arguments.</summary>
    public static Func<ChatResponse> CallTool(string toolName, Dictionary<string, object?>? arguments = null) =>
        () => new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent($"call-{toolName}", toolName, new Dictionary<string, object?>(arguments ?? []))]));

    /// <summary>Creates a final text answer.</summary>
    public static Func<ChatResponse> Answer(string text) => () => new(new ChatMessage(ChatRole.Assistant, text));

    private sealed class ScriptedChatClient(Func<ChatResponse>[] responses) : IChatClient
    {
        private int _index;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)]());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
