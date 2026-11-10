using Azure.Core;

namespace CasCap.Services;

/// <summary>Authenticates runtime requests to an Azure OpenAI realtime resource with an Entra token or an API key.</summary>
internal sealed class RealtimeProviderCredential
{
    private const string TokenScope = "https://ai.azure.com/.default";

    private readonly TokenCredential? _tokenCredential;
    private readonly string? _apiKey;

    private RealtimeProviderCredential(TokenCredential? tokenCredential, string? apiKey)
    {
        _tokenCredential = tokenCredential;
        _apiKey = apiKey;
    }

    /// <summary>Creates a credential that sends a bearer token for the runtime's Entra identity.</summary>
    public static RealtimeProviderCredential FromTokenCredential(TokenCredential tokenCredential) =>
        new(tokenCredential ?? throw new ArgumentNullException(nameof(tokenCredential)), null);

    /// <summary>Creates a credential that sends the resource API key.</summary>
    public static RealtimeProviderCredential FromApiKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new(null, apiKey);
    }

    /// <summary>Gets the authentication header name and value for one request or connection.</summary>
    public async ValueTask<KeyValuePair<string, string>> GetHeaderAsync(CancellationToken cancellationToken)
    {
        if (_tokenCredential is null)
            return new("api-key", _apiKey!);
        var token = await _tokenCredential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        return new("Authorization", $"Bearer {token.Token}");
    }
}
