using CasCap.Constants;
using System.Net;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Uses explicitly provisioned single-model llama.cpp embeddings without model loading routes or replay.</summary>
internal sealed class DocumentLlamaCppEmbeddingProvider(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory) : IDocumentEmbeddingProvider
{
    /// <inheritdoc/>
    public async Task VerifyAsync(DocumentIndexingManifest manifest, CancellationToken token)
    {
        using var client = Client(manifest);
        await VerifyModelAsync(client, manifest.Profile, token);
    }

    /// <inheritdoc/>
    public async Task<DocumentEmbeddingResult> GenerateAsync(DocumentEmbeddingBatch batch, CancellationToken token)
    {
        var profile = batch.Claim.Manifest.Profile;
        if (batch.Inputs.Count == 0 || batch.Inputs.Count > profile.MaximumBatchInputs
            || batch.Inputs.Any(input => string.IsNullOrWhiteSpace(input.Chunk.EmbeddingText)))
            throw new InvalidDataException("The llama.cpp batch exceeds its authorized input bounds or contains empty text.");
        using var client = Client(batch.Claim.Manifest);
        await VerifyModelAsync(client, profile, token);
        using var request = new HttpRequestMessage(HttpMethod.Post, DocumentLlamaCppEmbeddingRoutes.Embed)
        {
            Content = JsonContent.Create(new
            {
                model = profile.Model,
                input = batch.Inputs.Select(input => input.Chunk.EmbeddingText).ToArray(),
                encoding_format = "float",
            }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        using var json = await DocumentProviderJson.ReadAsync(response, 16 * 1024 * 1024, token);
        var result = Parse(json.RootElement, profile.Model, batch.Inputs.Count, profile.Dimensions);
        await VerifyModelAsync(client, profile, token);
        return result;
    }

    /// <summary>Reuses indexed finite-vector validation and requires the exact model and positive native usage.</summary>
    internal static DocumentEmbeddingResult Parse(JsonElement root, string model, int inputCount, int dimensions)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("model", out var returnedModel) || returnedModel.ValueKind != JsonValueKind.String
            || returnedModel.GetString() != model)
            throw new InvalidDataException("The llama.cpp response lacks the exact authorized model alias.");
        var result = DocumentEmbeddingProvider.Parse(root, inputCount, dimensions);
        if (result.InputTokens <= 0)
            throw new InvalidDataException("The llama.cpp response lacks a positive native input-token receipt.");
        return result;
    }

    private HttpClient Client(DocumentIndexingManifest manifest)
    {
        var binding = manifest.Profile.LlamaCpp ?? throw new InvalidDataException("The llama.cpp embedding binding is absent.");
        Validator.ValidateObject(binding, new ValidationContext(binding), validateAllProperties: true);
        if (string.IsNullOrEmpty(manifest.Profile.ModelRevision) || manifest.Profile.ModelRevision.Length != 64
            || manifest.Profile.ModelRevision.Any(character => !char.IsAsciiHexDigitLower(character))
            || manifest.Profile.Model != $"{DocumentLlamaCppEmbeddingRoutes.ModelAliasPrefix}{manifest.Profile.ModelRevision}")
            throw new InvalidDataException("The llama.cpp embedding alias is not bound to a complete GGUF SHA256 revision.");
        return DocumentEmbeddingProvider.LocalClient(serviceProvider, httpClientFactory, manifest, binding.Endpoint, AgentType.OpenAI);
    }

    private static async Task VerifyModelAsync(HttpClient client, DocumentEmbeddingProfileConfig profile, CancellationToken token)
    {
        using var modelsResponse = await client.GetAsync(DocumentLlamaCppEmbeddingRoutes.Models, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(modelsResponse);
        using var models = await DocumentProviderJson.ReadAsync(modelsResponse, 1024 * 1024, token);
        var root = models.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() != 1
            || data[0].ValueKind != JsonValueKind.Object
            || !data[0].TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != profile.Model
            || !data[0].TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("n_embd", out var length) || length.ValueKind != JsonValueKind.Number
            || !length.TryGetInt32(out var dimensions) || dimensions != profile.Dimensions)
            throw new InvalidDataException("llama.cpp must advertise exactly the pinned single model with compatible native dimensions.");
        using var propertiesResponse = await client.GetAsync(DocumentLlamaCppEmbeddingRoutes.Properties, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(propertiesResponse);
        using var properties = await DocumentProviderJson.ReadAsync(propertiesResponse, 1024 * 1024, token);
        if (properties.RootElement.ValueKind != JsonValueKind.Object
            || !properties.RootElement.TryGetProperty("model_alias", out var alias) || alias.ValueKind != JsonValueKind.String
            || alias.GetString() != profile.Model)
            throw new InvalidDataException("The llama.cpp endpoint is not the authorized single-model server; routers and implicit model selection are unsupported.");
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("llama.cpp did not return valid embedding metadata/results.", null, response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new InvalidDataException("llama.cpp did not return JSON embedding metadata/results.");
    }
}
