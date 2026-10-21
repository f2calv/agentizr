using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;

namespace CasCap.AgentRuntime.Execution.Tests.Integration;

/// <summary>Environment-provided settings for local Azure OpenAI integration tests.</summary>
public sealed record LocalAzureOpenAiTestSettings
{
    /// <summary>Gets the Azure OpenAI resource endpoint.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the model deployment name.</summary>
    public required string DeploymentName { get; init; }

    /// <summary>Gets the optional service principal tenant identifier.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the optional service principal client identifier.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the optional thumbprint of the service principal certificate in the current user store.</summary>
    public string? CertificateThumbprint { get; init; }

    /// <summary>Gets the request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates a certificate credential when a service principal is configured; otherwise uses the
    /// signed-in Azure CLI account.
    /// </summary>
    public TokenCredential CreateCredential()
    {
        if (string.IsNullOrWhiteSpace(CertificateThumbprint))
            return new AzureCliCredential();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, CertificateThumbprint, validOnly: false);
        Assert.True(certificates.Count == 1, "The configured service principal certificate was not found in the current user store.");
        return new ClientCertificateCredential(TenantId, ClientId, certificates[0]);
    }

    /// <summary>Loads settings without persisting or exposing endpoint details.</summary>
    public static bool TryLoad(out LocalAzureOpenAiTestSettings? settings)
    {
        var endpointValue = Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_OPENAI_ENDPOINT");
        var deploymentName = Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_OPENAI_DEPLOYMENT");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(deploymentName))
        {
            settings = null;
            return false;
        }

        settings = new LocalAzureOpenAiTestSettings
        {
            Endpoint = endpoint,
            DeploymentName = deploymentName,
            TenantId = Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_TENANT_ID"),
            ClientId = Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_CLIENT_ID"),
            CertificateThumbprint = Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_CERT_THUMBPRINT"),
            Timeout = int.TryParse(
                Environment.GetEnvironmentVariable("CASCAP_LOCAL_AZURE_OPENAI_TIMEOUT_SECONDS"),
                out var timeoutSeconds)
                && timeoutSeconds > 0
                    ? TimeSpan.FromSeconds(timeoutSeconds)
                    : TimeSpan.FromMinutes(2),
        };
        var servicePrincipalValues = new[] { settings.TenantId, settings.ClientId, settings.CertificateThumbprint };
        if (servicePrincipalValues.Any(string.IsNullOrWhiteSpace) && !servicePrincipalValues.All(string.IsNullOrWhiteSpace))
        {
            settings = null;
            return false;
        }
        return true;
    }
}
