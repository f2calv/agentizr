#if DEBUG
using CasCap.Constants;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Uses already-installed local Ollama embedding models, with digest checks and no truncation or replay.</summary>
internal sealed class DocumentOllamaEmbeddingProvider(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory) : IDocumentEmbeddingProvider
{
    private static readonly string[] _remoteProperties = ["remote_host", "remote_model"];

    /// <inheritdoc/>
    public async Task VerifyAsync(DocumentIndexingManifest manifest, CancellationToken token)
    {
        using var client = Client(manifest);
        await VerifyTagAsync(client, manifest.Profile, token);
        using var request = new HttpRequestMessage(HttpMethod.Post, DocumentOllamaEmbeddingRoutes.Show)
        {
            Content = JsonContent.Create(new { model = manifest.Profile.Model }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        using var json = await DocumentProviderJson.ReadAsync(response, 1024 * 1024, token);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || IsRemote(root)
            || !root.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array
            || !capabilities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == "embedding")
            || !root.TryGetProperty("model_info", out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("general.architecture", out var architecture) || architecture.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(architecture.GetString())
            || !info.TryGetProperty($"{architecture.GetString()}.embedding_length", out var length) || length.ValueKind != JsonValueKind.Number
            || !length.TryGetInt32(out var dimensions) || dimensions != manifest.Profile.Dimensions)
            throw new InvalidDataException("The installed Ollama model is not a local embedding model with the configured native dimensions.");
        await VerifyTagAsync(client, manifest.Profile, token);
    }

    /// <inheritdoc/>
    public async Task<DocumentEmbeddingResult> GenerateAsync(DocumentEmbeddingBatch batch, CancellationToken token)
    {
        var profile = batch.Claim.Manifest.Profile;
        using var client = Client(batch.Claim.Manifest);
        await VerifyTagAsync(client, profile, token);
        using var request = new HttpRequestMessage(HttpMethod.Post, DocumentOllamaEmbeddingRoutes.Embed)
        {
            Content = JsonContent.Create(new
            {
                model = profile.Model,
                input = batch.Inputs.Select(input => input.Chunk.EmbeddingText).ToArray(),
                truncate = false,
                keep_alive = "0",
            }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        using var json = await DocumentProviderJson.ReadAsync(response, 16 * 1024 * 1024, token);
        var result = Parse(json.RootElement, profile.Model, batch.Inputs.Count, profile.Dimensions);
        await VerifyTagAsync(client, profile, token);
        return result;
    }

    /// <summary>Validates an ordered native Ollama response without accepting truncated or missing results.</summary>
    internal static DocumentEmbeddingResult Parse(JsonElement root, string model, int inputCount, int dimensions)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("model", out var returnedModel) || returnedModel.ValueKind != JsonValueKind.String
            || returnedModel.GetString() != model
            || !root.TryGetProperty("embeddings", out var embeddings) || embeddings.ValueKind != JsonValueKind.Array
            || embeddings.GetArrayLength() != inputCount
            || !root.TryGetProperty("prompt_eval_count", out var usage) || usage.ValueKind != JsonValueKind.Number
            || !usage.TryGetInt64(out var inputTokens) || inputTokens <= 0)
            throw new InvalidDataException("The Ollama response lacks exact model/results or a positive native input-token receipt.");
        return new(embeddings.EnumerateArray().Select(vector => DocumentEmbeddingProvider.ParseVector(vector, dimensions)).ToArray(), inputTokens);
    }

    private HttpClient Client(DocumentIndexingManifest manifest)
    {
        var binding = manifest.Profile.Ollama ?? throw new InvalidDataException("The Ollama embedding binding is absent.");
        Validator.ValidateObject(binding, new ValidationContext(binding), validateAllProperties: true);
        if (!manifest.Profile.HasExecutionBinding)
            throw new InvalidDataException("The embedding provider binding is ambiguous.");
        var endpoint = new Uri(binding.Endpoint);
        var runtime = serviceProvider.GetRequiredService<IOptions<AgentRuntimeConfig>>().Value;
        var policy = serviceProvider.GetRequiredService<IOptions<AgentDefinitionPolicyConfig>>().Value;
        if (!runtime.Tenants.TryGetValue(manifest.TenantId, out var tenant)
            || !tenant.Providers.TryGetValue(manifest.Profile.Provider, out var provider) || provider.Type != AgentType.Ollama
            || provider.Endpoint != endpoint
            || !policy.AllowedProviderAuthorities.Contains(endpoint.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(provider.ApiKey) || tenant.EntraIdProviders.Contains(manifest.Profile.Provider))
            throw new InvalidDataException("The exact local Ollama endpoint/provider is not authorized for credential-free access by this tenant.");
        var client = httpClientFactory.CreateClient(nameof(DocumentEmbeddingProvider));
        client.BaseAddress = endpoint;
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private static async Task VerifyTagAsync(HttpClient client, DocumentEmbeddingProfileConfig profile, CancellationToken token)
    {
        using var response = await client.GetAsync(DocumentOllamaEmbeddingRoutes.Tags, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        using var json = await DocumentProviderJson.ReadAsync(response, 1024 * 1024, token);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Ollama did not return a local model inventory.");
        var matches = models.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == profile.Model).ToArray();
        if (matches.Length != 1 || IsRemote(matches[0])
            || !matches[0].TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String
            || digest.GetString() != profile.ModelRevision)
            throw new InvalidDataException("The exact local Ollama model tag/digest is absent, changed or remotely hosted.");
    }

    private static bool IsRemote(JsonElement root) =>
        _remoteProperties.Any(name => root.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null && (value.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(value.GetString())));

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("Ollama did not return a valid embedding metadata/result response.", null, response.StatusCode);
    }
}
#endif
