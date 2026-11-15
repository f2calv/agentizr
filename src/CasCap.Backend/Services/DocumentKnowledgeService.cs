using CasCap.AgentRuntime.Contracts.V1;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Bridges a bounded search query to existing Azure embeddings and current authorized pgvector passages.</summary>
public sealed class DocumentKnowledgeService
{
    private readonly ILogger<DocumentKnowledgeService> _logger;
    private readonly IOptions<DocumentKnowledgeConfig> _config;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentDefinitionStore _definitionStore;
    private readonly DocumentKnowledgeStore? _store;
    private readonly DocumentEmbeddingProvider _provider;
    private readonly DocumentChunkingService _chunking;

    internal DocumentKnowledgeService(
        ILogger<DocumentKnowledgeService> logger,
        IOptions<DocumentKnowledgeConfig> config,
        ITenantContext tenantContext,
        IAgentDefinitionStore definitionStore,
        DocumentKnowledgeStore? store,
        DocumentEmbeddingProvider provider,
        DocumentChunkingService chunking)
    {
        _logger = logger;
        _config = config;
        _tenantContext = tenantContext;
        _definitionStore = definitionStore;
        _store = store;
        _provider = provider;
        _chunking = chunking;
    }

    /// <summary>Embeds at most one short query and returns only currently authorized exact cosine matches.</summary>
    public async Task<DocumentKnowledgeOutcome> SearchAsync(
        string agentName, SearchKnowledgeRequest request, CancellationToken token)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_config.Value.TimeoutSeconds));
        try
        {
            var binding = await BindingAsync(agentName, deadline.Token);
            if (binding is null)
                return new();
            var originalBinding = binding;
            if (request.MaximumCefrLevel is { } level)
            {
                if (!DocumentKnowledgeStore.AllowsLevel(binding.MaximumCefrLevel, level))
                    return new() { ValidationError = "The requested CEFR level exceeds the agent's knowledge grant." };
                binding = binding with { MaximumCefrLevel = level };
            }
            var generations = await _store!.ReadAsync(binding, deadline.Token);
            if (generations.Count == 0)
                return new() { Response = new() { Matches = [], EmbeddingInputTokens = 0 } };
            var profile = generations[0].Manifest.Profile;
            if (profile.Azure is null || !profile.HasExecutionBinding
                || generations.Any(item => JsonSerializer.Serialize(item.Manifest.Profile, JsonSerializerOptions.Web)
                    != JsonSerializer.Serialize(profile, JsonSerializerOptions.Web)))
                throw new InvalidDataException("Query retrieval requires one compatible Azure embedding profile.");
            var queryTokens = _chunking.CountQueryTokens(request.Query, profile);
            if (queryTokens < 1 || queryTokens > Math.Min(binding.MaximumQueryTokens, profile.MaximumTokensPerInput))
                return new() { ValidationError = "The search query exceeds the configured embedding-token ceiling." };
            await _provider.VerifyAzureAsync(binding.TenantId, profile, deadline.Token);
            // TODO: Add a durable per-binding query allowance before enabling unattended or broadly shared retrieval.
            // Per-request ceilings and no replay bound one call, not aggregate spend across replicas or client retries.
            var result = await _provider.GenerateAzureAsync(binding.TenantId, profile, Guid.NewGuid(), [request.Query], deadline.Token);
            if (result.InputTokens > Math.Min(binding.MaximumQueryTokens, profile.MaximumTokensPerInput))
                throw new InvalidDataException("Native query usage exceeds the configured input-token ceiling.");
            if (await BindingAsync(agentName, deadline.Token) != originalBinding)
                return new();
            var matches = await _store.SearchAsync(binding, generations, result.Vectors[0], request.MaximumResults, deadline.Token);
            return new() { Response = new() { Matches = matches, EmbeddingInputTokens = result.InputTokens } };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("{ClassName} search exceeded its deadline; automatic inference replay disabled", nameof(DocumentKnowledgeService));
            return new() { TimedOut = true };
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("{ClassName} could not verify or retrieve knowledge; failure type {FailureType}; automatic inference replay disabled",
                    nameof(DocumentKnowledgeService), exception.GetType().Name);
            return new() { ProviderFailed = true };
        }
    }

    /// <summary>Reauthorizes a citation without inference or disclosure of internal storage coordinates.</summary>
    public async Task<KnowledgePassageResponse?> ReadPassageAsync(
        string agentName, string indexJobId, int ordinal, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_config.Value.TimeoutSeconds));
        var binding = await BindingAsync(agentName, deadline.Token);
        return binding is null ? null : await _store!.ReadPassageAsync(binding, indexJobId, ordinal, deadline.Token);
    }

    private async Task<DocumentKnowledgeBindingConfig?> BindingAsync(string agentName, CancellationToken token)
    {
        if (!_config.Value.Enabled || _store is null)
            return null;
        var binding = _config.Value.Bindings.SingleOrDefault(item =>
            item.TenantId == _tenantContext.TenantId && item.AgentName == agentName);
        return binding is not null && await _definitionStore.GetAsync(agentName, token) is not null ? binding : null;
    }

    internal async Task<bool> IsDefinitionCurrentAsync(string agentName, string version, CancellationToken token) =>
        (await _definitionStore.GetAsync(agentName, token))?.Version == version;
}
