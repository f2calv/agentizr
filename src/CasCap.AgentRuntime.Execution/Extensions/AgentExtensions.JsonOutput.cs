using System.Text.Json;

namespace CasCap.AgentRuntime.Execution.Extensions;

public static partial class AgentExtensions
{
    private sealed class JsonOutputChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            if (!IsCompletedResponsesResult(response))
                throw new InvalidOperationException("The provider did not complete the structured response; do not replay without authorization.");
            if (options?.Tools is { Count: > 0 }
                && response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any())
                return response;
            try
            {
                using var document = JsonDocument.Parse(response.Text);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("The provider returned invalid structured JSON.");
            }
            return response;
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Structured-output agents require a complete, non-streaming response.");
    }
}
