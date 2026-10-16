namespace CasCap.Abstractions;

/// <summary>Resolves provider credentials for the current tenant without exposing them through definitions.</summary>
public interface IProviderCredentialStore
{
    /// <summary>Gets the provider API key, or <see langword="null" /> when the provider needs no key.</summary>
    public ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the runtime's Microsoft Entra credential when the current tenant authenticates
    /// <paramref name="providerName"/> with it, otherwise <see langword="null" />.
    /// </summary>
    public Azure.Core.TokenCredential? GetTokenCredential(string providerName);
}
