using System.Text.Json;

namespace CasCap.AgentRuntime.Execution.Extensions;

public static partial class AgentExtensions
{
    private sealed class JsonOutputChatClient(
        IChatClient innerClient,
        Func<AgentStructuredResponse, CancellationToken, ValueTask>? observer) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            var completed = IsCompletedResponsesResult(response);
            if (observer is not null)
                await observer(new AgentStructuredResponse
                {
                    Text = response.Text,
                    IsComplete = completed,
                    ModelId = response.ModelId,
                    ResponseId = response.ResponseId,
                    Usage = response.Usage,
                }, cancellationToken).ConfigureAwait(false);
            if (!completed)
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
