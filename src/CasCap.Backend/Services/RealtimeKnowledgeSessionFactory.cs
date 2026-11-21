using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Extensions;
using System.Data.Common;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Creates call-owned knowledge state without retaining the authenticating request's scoped services.</summary>
internal sealed class RealtimeKnowledgeSessionFactory(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DocumentKnowledgeConfig> config,
    IOptions<DocumentIndexingConfig> indexing,
    ILogger<RealtimeKnowledgeSessionFactory> logger)
{
    public RealtimeKnowledgeSession? Create(string tenantId, AgentDefinition definition)
    {
        var binding = Binding(tenantId, definition.Name);
        if (binding is not { RealtimeEnabled: true, MaximumCefrLevel: "A1" })
            return null;
        return new(TimeSpan.FromMinutes(binding.MaximumRealtimeDurationMinutes), SearchAsync, RevalidateAsync)
        {
            PublicReferencePolicy = PublicReferencePolicy(binding),
        };

        async Task<DocumentKnowledgeOutcome> SearchAsync(SearchKnowledgeRequest request, CancellationToken token)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = CreateService(scope.ServiceProvider, tenantId);
            return binding == Binding(tenantId, definition.Name)
                && await service.IsCurrentAsync(definition.Name, definition.Version, binding, token)
                ? await service.SearchBoundAsync(definition.Name, request, binding, token) : new();
        }

        async Task<bool> RevalidateAsync(IReadOnlyList<KnowledgePassageResponse> passages, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(config.CurrentValue.TimeoutSeconds));
                if (binding != Binding(tenantId, definition.Name))
                    return false;
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = CreateService(scope.ServiceProvider, tenantId);
                if (!await service.IsCurrentAsync(definition.Name, definition.Version, binding, deadline.Token))
                    return false;
                foreach (var passage in passages)
                {
                    var current = await service.ReadBoundPassageAsync(
                        definition.Name, passage.IndexJobId, passage.ChunkOrdinal, binding, deadline.Token);
                    if (current is null || current.SourceSha256 != passage.SourceSha256
                        || current.DocumentId != passage.DocumentId)
                        return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is DbException or InvalidDataException or JsonException or OptionsValidationException
                || exception is OperationCanceledException && !token.IsCancellationRequested)
            {
                logger.LogWarning("{ClassName} could not revalidate realtime evidence; failure type {FailureType}",
                    nameof(RealtimeKnowledgeSessionFactory), exception.GetType().Name);
                return false;
            }
        }
    }

    // Match the process-lifetime policy used by the knowledge store and its manifest checks.
    private DocumentIndexingCollectionConfig? PublicReferencePolicy(DocumentKnowledgeBindingConfig binding) =>
        indexing.Value.Enabled && indexing.Value.Collections.SingleOrDefault(item =>
            item.TenantId == binding.TenantId && item.CollectionId == binding.CollectionId)
            is { Access: DocumentCollectionAccess.PublicReference } policy ? policy : null;

    private DocumentKnowledgeBindingConfig? Binding(string tenantId, string agentName) =>
        config.CurrentValue.Enabled ? config.CurrentValue.Bindings.SingleOrDefault(binding =>
            binding.TenantId == tenantId && binding.AgentName == agentName) : null;

    private static DocumentKnowledgeService CreateService(IServiceProvider services, string tenantId)
    {
        var tenant = new FixedTenantContext(tenantId);
        var definitions = ActivatorUtilities.CreateInstance<PostgresAgentDefinitionStore>(services, tenant);
        return WebApplicationBuilderExtensions.CreateDocumentKnowledgeService(services, tenant, definitions);
    }
}
