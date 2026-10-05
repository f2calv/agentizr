using CasCap.Common.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Executes resolved definitions through the shared Common.AI agent primitives.</summary>
internal sealed class CommonAgentExecutor(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory) : IAgentExecutor
{
    /// <inheritdoc/>
    public async ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        var provider = context.Definition.Provider with { ApiKey = context.ProviderApiKey };
        var httpClient = httpClientFactory.CreateClient(nameof(CommonAgentExecutor));
        httpClient.BaseAddress = provider.Endpoint;
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var (_, agent, instructions) = AgentExtensions.CreateAgent(
            provider,
            context.Definition.Agent,
            httpClient,
            loggerFactory: loggerFactory);
        var chatOptions = AgentExtensions.BuildChatOptions(context.Definition.Agent, instructions);
        if (!string.IsNullOrWhiteSpace(context.Overrides.ModelName))
            chatOptions.ModelId = context.Overrides.ModelName;
        if (!string.IsNullOrWhiteSpace(context.Overrides.Instructions))
            chatOptions.Instructions = context.Overrides.Instructions;

        AgentSession? session = null;
        if (!string.IsNullOrWhiteSpace(context.SessionStateJson))
        {
            using var sessionDocument = JsonDocument.Parse(context.SessionStateJson);
            session = await agent.DeserializeSessionAsync(
                sessionDocument.RootElement.Clone(),
                JsonSerializerOptions.Web).AsTask();
        }

        var result = await agent.RunAnalysisAsync(
            provider,
            context.Definition.Agent,
            AgentExtensions.BuildChatMessage(context.Request.Input),
            chatOptions,
            session,
            cancellationToken: cancellationToken,
            logger: loggerFactory.CreateLogger<CommonAgentExecutor>());

        string? sessionStateJson = null;
        if (result.Session is not null)
        {
            var serialized = await agent.SerializeSessionAsync(result.Session, JsonSerializerOptions.Web).AsTask();
            sessionStateJson = serialized.GetRawText();
        }

        return new AgentExecutionResult
        {
            OutputText = result.OutputText,
            SessionStateJson = sessionStateJson,
        };
    }
}
