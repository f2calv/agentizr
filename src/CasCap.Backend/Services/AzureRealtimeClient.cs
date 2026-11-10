using CasCap.AgentRuntime.Contracts.V1.Constants;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>Calls the Azure OpenAI GA Realtime REST endpoints that broker a WebRTC call.</summary>
/// <remarks>Ephemeral keys, SDP and provider call identifiers are returned to the caller and never logged.</remarks>
internal sealed class AzureRealtimeClient(HttpClient httpClient)
{
    /// <summary>Mints a short-lived client secret bound to the complete session configuration.</summary>
    public async Task<string> CreateClientSecretAsync(
        Uri endpoint,
        RealtimeProviderCredential credential,
        JsonObject session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "openai/v1/realtime/client_secrets"))
        {
            Content = new StringContent(
                new JsonObject { ["session"] = session.DeepClone() }.ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };
        await AddCredentialAsync(request, credential, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "client secret");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("value", out var value) && value.GetString() is { Length: > 0 } secret
            ? secret
            : throw new HttpRequestException("The realtime client secret response contained no secret.");
    }

    /// <summary>Exchanges a WebRTC SDP offer for an answer, filtering the client data channel.</summary>
    /// <returns>The SDP answer and the provider call identifier for the control connection.</returns>
    public async Task<(string SdpAnswer, string ProviderCallId)> CreateCallAsync(
        Uri endpoint,
        string clientSecret,
        string sdpOffer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "openai/v1/realtime/calls?webrtcfilter=on"))
        {
            Content = new StringContent(sdpOffer, Encoding.UTF8, RealtimeMediaTypes.Sdp),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {clientSecret}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "call");
        var location = response.Headers.Location?.OriginalString
            ?? throw new HttpRequestException("The realtime call response contained no call location.");
        var answer = await response.Content.ReadAsStringAsync(cancellationToken);
        return (answer, location[(location.LastIndexOf('/') + 1)..]);
    }

    /// <summary>Asks the provider to end a call; best effort because the control connection is also closed.</summary>
    /// <returns>Whether the provider accepted the request.</returns>
    public async Task<bool> HangUpAsync(
        Uri endpoint,
        RealtimeProviderCredential credential,
        string providerCallId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(endpoint, $"openai/v1/realtime/calls/{Uri.EscapeDataString(providerCallId)}/hangup"));
        await AddCredentialAsync(request, credential, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Gets the WebSocket address of a call's server-side control connection.</summary>
    public static Uri GetControlUri(Uri endpoint, string providerCallId) => new UriBuilder(endpoint)
    {
        Scheme = "wss",
        Port = -1,
        Path = "openai/v1/realtime",
        Query = $"call_id={Uri.EscapeDataString(providerCallId)}",
    }.Uri;

    private static async ValueTask AddCredentialAsync(
        HttpRequestMessage request,
        RealtimeProviderCredential credential,
        CancellationToken cancellationToken)
    {
        var (name, value) = await credential.GetHeaderAsync(cancellationToken);
        request.Headers.TryAddWithoutValidation(name, value);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"The realtime {operation} request failed with status {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        if (response.StatusCode is HttpStatusCode.NoContent)
            throw new HttpRequestException($"The realtime {operation} response was empty.");
    }
}
