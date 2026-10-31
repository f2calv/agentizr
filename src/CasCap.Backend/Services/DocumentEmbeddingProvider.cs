#if DEBUG
using Azure.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Dispatches bounded tenant-authorized Azure or Ollama embeddings without transport replay.</summary>
internal sealed class DocumentEmbeddingProvider(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    DocumentPreparationModelVerifier documentPreparationModelVerifier,
    DocumentOllamaEmbeddingProvider documentOllamaEmbeddingProvider) : IDocumentEmbeddingProvider
{
    public async Task VerifyAsync(DocumentIndexingManifest manifest, CancellationToken token)
    {
        if (manifest.Profile.Ollama is not null)
        {
            await documentOllamaEmbeddingProvider.VerifyAsync(manifest, token);
            return;
        }
        var azure = manifest.Profile.Azure ?? throw new InvalidDataException("The Azure embedding binding is absent.");
        ValidateAuthority(manifest);
        var credentials = Credentials(manifest);
        if (credentials.GetTokenCredential(manifest.Profile.Provider) is null
            && string.IsNullOrWhiteSpace(await credentials.GetApiKeyAsync(manifest.Profile.Provider, token)))
            throw new InvalidDataException("The embedding provider has no authorized tenant credential.");
        await documentPreparationModelVerifier.VerifyAsync(new Uri(azure.Endpoint), azure.DeploymentName,
            azure.DeploymentResourceId, azure.ManagementEndpoint, manifest.Profile.Model, manifest.Profile.ModelRevision, token);
    }

    public async Task<DocumentEmbeddingResult> GenerateAsync(DocumentEmbeddingBatch batch, CancellationToken token)
    {
        if (batch.Claim.Manifest.Profile.Ollama is not null)
            return await documentOllamaEmbeddingProvider.GenerateAsync(batch, token);
        ValidateAuthority(batch.Claim.Manifest);
        var profile = batch.Claim.Manifest.Profile;
        var azure = profile.Azure ?? throw new InvalidDataException("The Azure embedding binding is absent.");
        var credentials = Credentials(batch.Claim.Manifest);
        using var client = httpClientFactory.CreateClient(nameof(DocumentEmbeddingProvider));
        client.BaseAddress = new Uri(azure.Endpoint);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"openai/deployments/{Uri.EscapeDataString(azure.DeploymentName)}/embeddings?api-version=2024-10-21");
        request.Headers.Add("x-ms-client-request-id", batch.Claim.ClaimId.ToString("D"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (credentials.GetTokenCredential(profile.Provider) is { } credential)
        {
            var access = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), token);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
        }
        else
        {
            var key = await credentials.GetApiKeyAsync(profile.Provider, token);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException("The embedding credential is no longer authorized.");
            request.Headers.Add("api-key", key);
        }
        request.Content = JsonContent.Create(new
        {
            input = batch.Inputs.Select(input => input.Chunk.EmbeddingText).ToArray(),
            dimensions = profile.Dimensions,
            encoding_format = "float",
        });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("The embedding submission did not return a valid receipt.", null, response.StatusCode);
        using var json = await DocumentProviderJson.ReadAsync(response, 16 * 1024 * 1024, token);
        return Parse(json.RootElement, batch.Inputs.Count, profile.Dimensions);
    }

    internal static DocumentEmbeddingResult Parse(JsonElement root, int inputCount, int dimensions)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != inputCount
            || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
            || !usage.TryGetProperty("prompt_tokens", out var prompt) || prompt.ValueKind != JsonValueKind.Number
            || !prompt.TryGetInt64(out var inputTokens) || inputTokens < 0
            || !usage.TryGetProperty("total_tokens", out var total) || total.ValueKind != JsonValueKind.Number
            || !total.TryGetInt64(out var totalTokens) || totalTokens != inputTokens)
            throw new InvalidDataException("The embedding response lacks a complete result or native input-token receipt.");
        var ordered = new float[inputCount][];
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number
                || !index.TryGetInt32(out var ordinal) || ordinal < 0 || ordinal >= inputCount
                || ordered[ordinal] is not null || !item.TryGetProperty("embedding", out var values))
                throw new InvalidDataException("The embedding result indexes or dimensions differ from the reserved inputs.");
            ordered[ordinal] = ParseVector(values, dimensions);
        }
        return new(ordered, inputTokens);
    }

    /// <summary>Checks native vector shape and cosine compatibility for either provider.</summary>
    internal static float[] ParseVector(JsonElement values, int dimensions)
    {
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != dimensions)
            throw new InvalidDataException("The embedding vector has incompatible dimensions.");
        var vector = new float[dimensions];
        var position = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number))
                throw new InvalidDataException("The embedding response contains a nonfinite or invalid vector component.");
            vector[position++] = number;
        }
        if (!vector.Any(value => value != 0))
            throw new InvalidDataException("A zero vector cannot support the configured cosine distance.");
        return vector;
    }

    private void ValidateAuthority(DocumentIndexingManifest manifest)
    {
        var azure = manifest.Profile.Azure ?? throw new InvalidDataException("The Azure embedding binding is absent.");
        if (!manifest.Profile.HasExecutionBinding)
            throw new InvalidDataException("The embedding provider binding is ambiguous.");
        Validator.ValidateObject(azure, new ValidationContext(azure), validateAllProperties: true);
        var endpoint = new Uri(azure.Endpoint);
        var runtime = serviceProvider.GetRequiredService<IOptions<AgentRuntimeConfig>>().Value;
        var policy = serviceProvider.GetRequiredService<IOptions<AgentDefinitionPolicyConfig>>().Value;
        if (!runtime.Tenants.TryGetValue(manifest.TenantId, out var tenant)
            || !tenant.Providers.TryGetValue(manifest.Profile.Provider, out var provider) || provider.Type != AgentType.AzureOpenAI
            || provider.Endpoint != endpoint
            || !policy.AllowedProviderAuthorities.Contains(endpoint.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The exact Azure embedding endpoint/provider is not authorized for this tenant.");
    }

    private ConfigurationProviderCredentialStore Credentials(DocumentIndexingManifest manifest) =>
        ActivatorUtilities.CreateInstance<ConfigurationProviderCredentialStore>(serviceProvider, new EmbeddingTenant(manifest.TenantId));

    private sealed class EmbeddingTenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
#endif
