namespace CasCap.Abstractions;

/// <summary>Resolves provider credentials for the current tenant without exposing them through definitions.</summary>
public interface IProviderCredentialStore
{
    /// <summary>Gets the provider API key, or <see langword="null" /> when the provider needs no key.</summary>
    ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken);
}
