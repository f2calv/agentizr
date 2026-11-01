using Azure.Core;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

#pragma warning disable OPENAI001 // The opt-in Responses adapter uses the installed SDK's experimental API.

namespace CasCap.AgentRuntime.Execution.Extensions;

public static partial class AgentExtensions
{
    private static bool IsCompletedResponsesResult(ChatResponse response) =>
        response.RawRepresentation is ResponseResult { Status: ResponseStatus.Completed, Error: null };

    private static void ConfigureTransientResponseOptions(ChatOptions options, JsonOutputConfig? output)
    {
        // The generic schema mapper omits strict mode and would replace the SDK format below.
        if (output is not null)
            options.ResponseFormat = null;
        options.RawRepresentationFactory = _ => new CreateResponseOptions
        {
            StoredOutputEnabled = false,
            BackgroundModeEnabled = false,
            TextOptions = output is null ? null : new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    output.Name, BinaryData.FromString(output.Schema.GetRawText()), null, true),
            },
        };
    }

    private static ChatClientBuilder CreateResponsesChatClient(
        ProviderConfig provider,
        AgentConfig agentConfig,
        HttpClient httpClient,
        TokenCredential? tokenCredential)
    {
        if (provider.Type is not (AgentType.AzureOpenAI or AgentType.OpenAI))
            throw new NotSupportedException("The Responses API requires an OpenAI or Azure OpenAI provider.");

        var endpoint = provider.Endpoint;
        if (provider.Type == AgentType.AzureOpenAI)
        {
            if (endpoint is not { IsAbsoluteUri: true, Scheme: "https" } || endpoint.AbsolutePath != "/"
                || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
                throw new InvalidOperationException("Azure OpenAI Responses requires a credential-free root endpoint.");
            endpoint = new Uri(endpoint, "openai/v1/");
        }

        var options = new ResponsesClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            // A failed paid POST may already have completed. Only the job owner may authorize replay.
            RetryPolicy = new ClientRetryPolicy(0),
        };
        AuthenticationPolicy authentication = provider.Type == AgentType.AzureOpenAI && tokenCredential is not null
            ? new AzureResponsesAuthenticationPolicy(tokenCredential)
            : ApiKeyAuthenticationPolicy.CreateBearerAuthorizationPolicy(new ApiKeyCredential(
                provider.ApiKey ?? throw new InvalidOperationException(
                    $"Agent '{agentConfig.Name}' requires a provider credential for the Responses API.")));
        return new ResponsesClient(authentication, options).AsIChatClient(provider.ModelName)
            .AsBuilder().Use(inner => new ResponsesPolicyChatClient(inner, agentConfig, provider.ReasoningEffort));
    }

    private sealed class ResponsesPolicyChatClient(
        IChatClient innerClient,
        AgentConfig config,
        ReasoningEffort? reasoningEffort) : DelegatingChatClient(innerClient)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(messages, PrepareOptions(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(messages, PrepareOptions(options), cancellationToken);

        private ChatOptions PrepareOptions(ChatOptions? options)
        {
            var request = options?.Clone() ?? new ChatOptions();
            ConfigureTransientResponseOptions(request, config.JsonOutput);
            if (request.Reasoning is null && reasoningEffort is { } effort)
                request.Reasoning = new ReasoningOptions { Effort = effort };
            if (config.MaximumOutputTokens is { } ceiling)
                request.MaxOutputTokens = Math.Min(request.MaxOutputTokens ?? ceiling, ceiling);
            return request;
        }
    }

    private sealed class AzureResponsesAuthenticationPolicy(TokenCredential credential) : AuthenticationPolicy
    {
        private static readonly TokenRequestContext TokenContext = new(["https://ai.azure.com/.default"]);

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            var token = credential.GetToken(TokenContext, message.CancellationToken);
            message.Request.Headers.Set("Authorization", $"Bearer {token.Token}");
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            var token = await credential.GetTokenAsync(TokenContext, message.CancellationToken);
            message.Request.Headers.Set("Authorization", $"Bearer {token.Token}");
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
