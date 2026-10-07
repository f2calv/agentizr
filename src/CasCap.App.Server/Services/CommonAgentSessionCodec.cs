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
            Entries = GetStateBagEntries(session),
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
        if (!TryCompactSession(session, retainMessageCount, out var removedMessageCount))
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

    private static bool TryCompactSession(AgentSession session, int retainMessageCount, out int removedMessageCount)
    {
        removedMessageCount = 0;
        if (!session.TryGetInMemoryChatHistory(out var messages))
            return false;

        var excess = messages.Count - retainMessageCount;
        if (excess <= 0)
            return true;

        messages.RemoveRange(0, excess);
        session.SetInMemoryChatHistory(messages);
        removedMessageCount = excess;
        return true;
    }

    private static List<AgentSessionEntryInspection> GetStateBagEntries(AgentSession session)
    {
        var entries = new List<AgentSessionEntryInspection>();
        try
        {
            foreach (var property in session.StateBag.Serialize().EnumerateObject())
            {
                var messageCount = 0;
                var userMessageCount = 0;
                var assistantMessageCount = 0;
                if (property.Value.ValueKind is JsonValueKind.Object
                    && (property.Value.TryGetProperty("Messages", out var messages)
                        || property.Value.TryGetProperty("messages", out messages))
                    && messages.ValueKind is JsonValueKind.Array)
                {
                    messageCount = messages.GetArrayLength();
                    foreach (var message in messages.EnumerateArray())
                    {
                        if (!(message.TryGetProperty("Role", out var role)
                            || message.TryGetProperty("role", out role))
                            || role.ValueKind is not JsonValueKind.String)
                            continue;
                        if (string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase))
                            userMessageCount++;
                        else if (string.Equals(role.GetString(), "assistant", StringComparison.OrdinalIgnoreCase))
                            assistantMessageCount++;
                    }
                }

                entries.Add(new AgentSessionEntryInspection
                {
                    Key = string.IsNullOrEmpty(property.Name) ? "(default)" : property.Name,
                    ByteSize = Encoding.UTF8.GetByteCount(property.Value.GetRawText()),
                    MessageCount = messageCount,
                    UserMessageCount = userMessageCount,
                    AssistantMessageCount = assistantMessageCount,
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }
        return entries;
    }
}
