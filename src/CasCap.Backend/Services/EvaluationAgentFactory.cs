using CasCap.Common.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CasCap.Services;

/// <summary>Creates evaluation agents through the same provider construction as runtime execution.</summary>
internal sealed class EvaluationAgentFactory(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IProviderCredentialStore credentialStore) : IEvaluationAgentFactory
{
    /// <inheritdoc/>
    public (AIAgent Agent, string Instructions) Create(ProviderConfig provider, AgentConfig agent, List<AITool> tools)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(CommonAgentExecutor));
        httpClient.BaseAddress = provider.Endpoint;
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var (_, aiAgent, instructions) = AgentExtensions.CreateAgent(
            provider,
            agent,
            httpClient,
            tools,
            tokenCredential: credentialStore.GetTokenCredential(agent.Provider),
            loggerFactory: loggerFactory);
        return (aiAgent, instructions);
    }
}
