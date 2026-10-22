using Microsoft.Extensions.AI;

namespace CasCap.Services;

/// <summary>Records text and tool-call projections for every model round-trip, before compaction can discard them.</summary>
internal sealed class AgentHistoryChatClient(IChatClient innerClient, string agentName, AgentRunScope scope)
    : DelegatingChatClient(innerClient)
{
    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        if (scope.OnActivity is { } sink)
            foreach (var message in response.Messages)
                await sink(new AgentRunActivity
                {
                    Kind = "model-message",
                    AgentName = agentName,
                    Depth = scope.Depth,
                    Data = new
                    {
                        Role = message.Role.Value,
                        Text = string.Join("\n", message.Contents.OfType<TextContent>().Select(content => content.Text)),
                        Calls = message.Contents.OfType<FunctionCallContent>().Select(call =>
                            new { call.CallId, call.Name, call.Arguments }),
                        response.ResponseId,
                        response.ModelId,
                        FinishReason = response.FinishReason?.Value,
                    },
                }, cancellationToken);
        return response;
    }
}
