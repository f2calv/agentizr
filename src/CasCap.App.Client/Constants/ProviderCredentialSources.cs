namespace CasCap.Client.Constants;

/// <summary>Wire values describing how the runtime authenticates to a provider.</summary>
public static class ProviderCredentialSources
{
    /// <summary>The runtime sends a configured API key.</summary>
    public const string ApiKey = nameof(ApiKey);

    /// <summary>The runtime uses its own Microsoft Entra identity.</summary>
    public const string EntraId = nameof(EntraId);

    /// <summary>The runtime sends no credential.</summary>
    public const string None = nameof(None);
}
