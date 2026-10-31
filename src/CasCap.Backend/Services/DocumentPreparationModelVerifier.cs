using Azure.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Reads bounded ARM metadata, binding the actual endpoint/deployment to the approved model revision.</summary>
internal sealed class DocumentPreparationModelVerifier(
    IHttpClientFactory httpClientFactory,
    RuntimeEntraCredential runtimeEntraCredential)
{
    /// <summary>Checks both resources before paid dispatch; metadata authorization failures are not bypassed.</summary>
    public Task VerifyAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken) =>
        VerifyAsync(plan.Definition.Provider.Endpoint!, plan.Definition.Provider.ModelName!,
            plan.Settings.DeploymentResourceId, plan.Settings.ManagementEndpoint,
            plan.Settings.Model, plan.Settings.ModelRevision, cancellationToken);

    /// <summary>Checks an explicit Azure endpoint/deployment/model binding without submitting model inputs.</summary>
    public async Task VerifyAsync(Uri providerEndpoint, string deploymentName, string deploymentResourceId,
        string managementEndpoint, string modelName, string modelRevision, CancellationToken cancellationToken)
    {
        var credential = runtimeEntraCredential.Credential
            ?? throw new InvalidDataException("Deployment verification requires the authorized runtime Azure identity.");
        var endpoint = new Uri(managementEndpoint);
        var token = await credential.GetTokenAsync(new TokenRequestContext([new Uri(endpoint, ".default").AbsoluteUri]), cancellationToken);
        using var client = httpClientFactory.CreateClient(nameof(DocumentPreparationModelVerifier));
        client.BaseAddress = endpoint;
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        var deploymentId = deploymentResourceId;
        var accountId = deploymentId[..deploymentId.LastIndexOf("/deployments/", StringComparison.OrdinalIgnoreCase)];
        using var account = await ReadAsync(client, accountId, cancellationToken);
        using var deployment = await ReadAsync(client, deploymentId, cancellationToken);
        if (!Matches(account.RootElement, "id", accountId, StringComparison.OrdinalIgnoreCase)
            || !Matches(deployment.RootElement, "id", deploymentId, StringComparison.OrdinalIgnoreCase)
            || !Matches(deployment.RootElement, "name", deploymentName, StringComparison.OrdinalIgnoreCase)
            || !account.RootElement.TryGetProperty("properties", out var accountProperties)
            || !deployment.RootElement.TryGetProperty("properties", out var deploymentProperties)
            || !Matches(accountProperties, "provisioningState", "Succeeded")
            || !Matches(deploymentProperties, "provisioningState", "Succeeded")
            || !EndpointMatches(accountProperties, providerEndpoint)
            || !deploymentProperties.TryGetProperty("model", out var model)
            || !Matches(model, "format", "OpenAI")
            || !Matches(model, "name", modelName)
            || !Matches(model, "version", modelRevision))
            throw new InvalidDataException("The deployed account, endpoint or model revision differs from the admitted preparation plan.");
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string resourceId, CancellationToken cancellationToken)
    {
        var path = string.Join('/', resourceId.Split('/').Select(Uri.EscapeDataString));
        using var response = await client.GetAsync(path.TrimStart('/') + "?api-version=2024-10-01",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("Azure deployment metadata could not be verified.", null, response.StatusCode);
        return await DocumentProviderJson.ReadAsync(response, 1_048_576, cancellationToken);
    }

    private static bool EndpointMatches(JsonElement properties, Uri expected) =>
        MatchesEndpoint(properties, "endpoint", expected)
        || properties.TryGetProperty("endpoints", out var endpoints) && endpoints.ValueKind == JsonValueKind.Object
            && endpoints.EnumerateObject().Any(item => IsEndpoint(item.Value, expected));

    private static bool MatchesEndpoint(JsonElement parent, string property, Uri expected) =>
        parent.TryGetProperty(property, out var value) && IsEndpoint(value, expected);

    private static bool IsEndpoint(JsonElement value, Uri expected) =>
        value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps && endpoint.UserInfo.Length == 0
        && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0 && endpoint.AbsolutePath == "/"
        && string.Equals(endpoint.AbsoluteUri, expected.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(JsonElement parent, string property, string? expected, StringComparison comparison = StringComparison.Ordinal) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), expected, comparison);
}
