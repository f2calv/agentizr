using CasCap.AgentRuntime.Client.Abstractions;
using System.Net;
using System.Net.Http.Json;

namespace CasCap.AgentRuntime.Client;

/// <summary>Typed HTTP implementation of <see cref="IAgentRuntimeClient" />.</summary>
public sealed class AgentRuntimeClient(HttpClient httpClient) : IAgentRuntimeClient
{
    /// <inheritdoc/>
    public async Task<RunAgentResponse?> RunAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        using var response = await httpClient.PostAsJsonAsync(
            $"api/v1/agents/{Uri.EscapeDataString(agentName)}/runs",
            request,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunAgentResponse>(cancellationToken)
            ?? throw new InvalidDataException("Agent Runtime returned an empty success response.");
    }
}
