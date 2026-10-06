using CasCap.Common.Extensions;
using Microsoft.Agents.AI;
using System.Text;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Inspects and compacts Agent Framework session payloads using their owning agent definition.</summary>
internal sealed class CommonAgentSessionCodec(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory) : IAgentSessionCodec
{
    /// <inheritdoc/>
    public async ValueTask<AgentSessionInspection> InspectAsync(
        AgentDefinition definition,
        string? providerApiKey,
        string sessionStateJson,
        CancellationToken cancellationToken)
    {
        var agent = CreateAgent(definition, providerApiKey);
        var session = await DeserializeAsync(agent, sessionStateJson, cancellationToken);
        return new AgentSessionInspection
        {
            SizeBytes = Encoding.UTF8.GetByteCount(sessionStateJson),
            Entries = ChatCommandParser.GetStateBagEntries(session),
        };
    }

    /// <inheritdoc/>
    public async ValueTask<AgentSessionCompactionResult> CompactAsync(
        AgentDefinition definition,
        string? providerApiKey,
        string sessionStateJson,
        int retainMessageCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retainMessageCount, 1);
        var agent = CreateAgent(definition, providerApiKey);
        var session = await DeserializeAsync(agent, sessionStateJson, cancellationToken);
        if (!ChatCommandParser.TryCompactSession(session, retainMessageCount, out var removedMessageCount))
        {
            return new AgentSessionCompactionResult
            {
                SessionStateJson = sessionStateJson,
            };
        }

        var serialized = await agent.SerializeSessionAsync(
            session,
            JsonSerializerOptions.Web,
            cancellationToken).AsTask();
        return new AgentSessionCompactionResult
        {
            HistoryAvailable = true,
            RemovedMessageCount = removedMessageCount,
            SessionStateJson = serialized.GetRawText(),
        };
    }

    private AIAgent CreateAgent(AgentDefinition definition, string? providerApiKey)
    {
        var provider = definition.Provider with { ApiKey = providerApiKey };
        var httpClient = httpClientFactory.CreateClient(nameof(CommonAgentExecutor));
        httpClient.BaseAddress = provider.Endpoint;
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var (_, agent, _) = AgentExtensions.CreateAgent(
            provider,
            definition.Agent,
            httpClient,
            tools: [],
            loggerFactory: loggerFactory);
        return agent;
    }

    private static async ValueTask<AgentSession> DeserializeAsync(
        AIAgent agent,
        string sessionStateJson,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(sessionStateJson);
        return await agent.DeserializeSessionAsync(
            document.RootElement.Clone(),
            JsonSerializerOptions.Web,
            cancellationToken);
    }
}
