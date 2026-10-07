namespace CasCap.Abstractions;

/// <summary>Resolves tenant-scoped remote MCP authorization without exposing secrets in definitions.</summary>
public interface IMcpCredentialStore
{
    /// <summary>Gets a complete Authorization header value for a logical credential name.</summary>
    public ValueTask<string?> GetAuthorizationHeaderAsync(
        string credentialName,
        CancellationToken cancellationToken);
}