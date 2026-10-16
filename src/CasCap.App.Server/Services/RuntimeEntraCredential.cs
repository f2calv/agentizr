using Azure.Core;

namespace CasCap.Services;

/// <summary>The runtime's own Microsoft Entra credential, offered to tenant providers that opt in.</summary>
/// <param name="credentialFactory">Creates the credential on first use, or returns <see langword="null"/> when none is configured.</param>
public sealed class RuntimeEntraCredential(Func<TokenCredential?> credentialFactory)
{
    private readonly Lazy<TokenCredential?> _credential = new(credentialFactory);

    /// <summary>Gets the credential, or <see langword="null"/> when the runtime has no Microsoft Entra identity.</summary>
    public TokenCredential? Credential => _credential.Value;
}
