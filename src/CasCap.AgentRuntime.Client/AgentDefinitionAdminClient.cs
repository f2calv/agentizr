using CasCap.AgentRuntime.Client.Abstractions;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using System.Net;
using System.Net.Http.Json;

namespace CasCap.AgentRuntime.Client;

/// <inheritdoc cref="IAgentDefinitionAdminClient" />
public sealed class AgentDefinitionAdminClient(HttpClient httpClient) : IAgentDefinitionAdminClient
{
    /// <inheritdoc/>
    public async Task<AgentDefinitionSnapshotResponse> PublishAsync(
        string agentName,
        PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            BuildPath(agentName, AgentRuntimeRoutes.Definitions),
            request,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<AgentDefinitionSnapshotResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<AgentDefinitionSnapshotResponse?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken) =>
        GetOptionalAsync<AgentDefinitionSnapshotResponse>(
            BuildPath(agentName, AgentRuntimeRoutes.ActiveDefinition),
            cancellationToken);

    /// <inheritdoc/>
    public Task<AgentDefinitionSnapshotResponse?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken) =>
        GetOptionalAsync<AgentDefinitionSnapshotResponse>(
            BuildPath(agentName, AgentRuntimeRoutes.DefinitionVersion, definitionVersion),
            cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentDefinitionHistoryItemResponse>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        using var response = await httpClient.GetAsync(
            $"{BuildPath(agentName, AgentRuntimeRoutes.Definitions)}?limit={limit}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<AgentDefinitionHistoryItemResponse[]>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        ActivateAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Put,
            BuildPath(agentName, AgentRuntimeRoutes.DefinitionActivation, definitionVersion))
        {
            Content = JsonContent.Create(request),
        };
        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentDefinitionActivationResponse>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        using var response = await httpClient.GetAsync(
            $"{BuildPath(agentName, AgentRuntimeRoutes.DefinitionActivations)}?limit={limit}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<AgentDefinitionActivationResponse[]>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidDataException("Agent Runtime returned an empty success response.");

    private static string BuildPath(string agentName, string route, string? definitionVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        var path = AgentRuntimeRoutes.AgentGroup.Replace(
            "{agentName}",
            Uri.EscapeDataString(agentName),
            StringComparison.Ordinal) + route;
        if (definitionVersion is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersion);
            path = path.Replace(
                "{definitionVersion}",
                Uri.EscapeDataString(definitionVersion),
                StringComparison.Ordinal);
        }
        return path.TrimStart('/');
    }
}
