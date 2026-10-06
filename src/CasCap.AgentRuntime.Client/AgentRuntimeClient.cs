using CasCap.AgentRuntime.Client.Abstractions;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
            BuildPath(agentName, AgentRuntimeRoutes.Runs),
            request,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunAgentResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Agent Runtime returned an empty success response.");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RunAgentStreamItem> StreamAgentAsync(
        string agentName,
        RunAgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildPath(agentName, AgentRuntimeRoutes.RunStream))
        {
            Content = JsonContent.Create(request),
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<RunAgentStreamItem>(
            responseStream,
            JsonSerializerOptions.Web,
            cancellationToken).ConfigureAwait(false))
        {
            if (item is not null)
                yield return item;
        }
    }

    /// <inheritdoc/>
    public Task<AgentSessionInfoResponse?> GetSessionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken) =>
        GetAsync<AgentSessionInfoResponse>(
            BuildPath(agentName, AgentRuntimeRoutes.Session, sessionId),
            cancellationToken);

    /// <inheritdoc/>
    public Task<bool> ResetSessionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(
            HttpMethod.Delete,
            BuildPath(agentName, AgentRuntimeRoutes.Session, sessionId),
            cancellationToken);

    /// <inheritdoc/>
    public Task<CompactAgentSessionResponse?> CompactSessionAsync(
        string agentName,
        string sessionId,
        CompactAgentSessionRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync<CompactAgentSessionRequest, CompactAgentSessionResponse>(
            HttpMethod.Post,
            BuildPath(agentName, AgentRuntimeRoutes.SessionCompaction, sessionId),
            request,
            cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SaveSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(
            HttpMethod.Put,
            BuildPath(agentName, AgentRuntimeRoutes.SessionSnapshot, sessionId, snapshotName),
            cancellationToken);

    /// <inheritdoc/>
    public Task<bool> LoadSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(
            HttpMethod.Post,
            BuildPath(agentName, AgentRuntimeRoutes.SessionSnapshotActivation, sessionId, snapshotName),
            cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(
            HttpMethod.Delete,
            BuildPath(agentName, AgentRuntimeRoutes.SessionSnapshot, sessionId, snapshotName),
            cancellationToken);

    /// <inheritdoc/>
    public Task<AgentOverridesResponse?> GetOverridesAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken) =>
        GetAsync<AgentOverridesResponse>(
            BuildPath(agentName, AgentRuntimeRoutes.SessionOverrides, sessionId),
            cancellationToken);

    /// <inheritdoc/>
    public Task<AgentOverridesResponse?> SetOverridesAsync(
        string agentName,
        string sessionId,
        UpdateAgentOverridesRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync<UpdateAgentOverridesRequest, AgentOverridesResponse>(
            HttpMethod.Put,
            BuildPath(agentName, AgentRuntimeRoutes.SessionOverrides, sessionId),
            request,
            cancellationToken);

    private async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(request),
        };
        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SendNoContentAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private static async Task<TResponse?> ReadResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Agent Runtime returned an empty success response.");
    }

    private static string BuildPath(
        string agentName,
        string route,
        string? sessionId = null,
        string? snapshotName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        var path = AgentRuntimeRoutes.AgentGroup.Replace(
            "{agentName}",
            Uri.EscapeDataString(agentName),
            StringComparison.Ordinal) + route;
        if (sessionId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            path = path.Replace("{sessionId}", Uri.EscapeDataString(sessionId), StringComparison.Ordinal);
        }
        if (snapshotName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
            path = path.Replace("{snapshotName}", Uri.EscapeDataString(snapshotName), StringComparison.Ordinal);
        }
        return path.TrimStart('/');
    }
}
