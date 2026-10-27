using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using Azure.Core;

namespace CasCap.Services;

/// <summary>Uses the shared layout client and actual runtime executor with explicit worker-local tenant credentials.</summary>
internal sealed class DocumentPreparationProvider(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    RuntimeEntraCredential runtimeEntraCredential,
    DocumentPreparationModelVerifier documentPreparationModelVerifier) : IDocumentPreparationProvider
{
    /// <inheritdoc/>
    public async Task VerifyModelAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken)
    {
        DocumentPreparationPlanService.Validate(plan.Definition, plan.Settings);
        var runtime = serviceProvider.GetRequiredService<IOptions<AgentRuntimeConfig>>().Value;
        var policy = serviceProvider.GetRequiredService<IOptions<AgentDefinitionPolicyConfig>>().Value;
        if (!runtime.Tenants.TryGetValue(plan.TenantId, out var tenant)
            || !tenant.Providers.ContainsKey(plan.Definition.Agent.Provider)
            || !policy.AllowedProviderAuthorities.Contains(
                plan.Definition.Provider.Endpoint!.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The pinned preparation provider is not currently authorized for this tenant.");
        var credentials = ActivatorUtilities.CreateInstance<ConfigurationProviderCredentialStore>(
            serviceProvider, new PreparationTenant(plan.TenantId));
        if (credentials.GetTokenCredential(plan.Definition.Agent.Provider) is null
            && string.IsNullOrWhiteSpace(await credentials.GetApiKeyAsync(plan.Definition.Agent.Provider, cancellationToken)))
            throw new InvalidDataException("The pinned preparation provider has no authorized credential.");
        await documentPreparationModelVerifier.VerifyAsync(plan, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Uri> SubmitLayoutAsync(
        DocumentPreparationPlan plan, byte[] pdf, int[] pages, CancellationToken cancellationToken)
    {
        using var client = await CreateLayoutHttpClientAsync(plan, cancellationToken);
        return await Layout(client, plan).SubmitAsync(pdf, pages, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DocumentLayoutPollResult> PollLayoutAsync(
        DocumentPreparationPlan plan, Uri operation, int[] pages, CancellationToken cancellationToken)
    {
        using var client = await CreateLayoutHttpClientAsync(plan, cancellationToken);
        return await Layout(client, plan).GetResultAsync(operation, pages, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ExecutePageAsync(
        DocumentPreparationPageRequest request,
        Func<AgentStructuredResponse, CancellationToken, ValueTask> observer,
        CancellationToken cancellationToken)
    {
        if (request.TenantId != request.Plan.TenantId || request.PageNumber < 1
            || request.PageNumber > request.Plan.Settings.MaximumPages)
            throw new InvalidOperationException("The page execution is outside its admitted tenant/page authorization.");
        var tenant = new PreparationTenant(request.TenantId);
        var credentials = ActivatorUtilities.CreateInstance<ConfigurationProviderCredentialStore>(serviceProvider, tenant);
        var definitions = ActivatorUtilities.CreateInstance<PostgresAgentDefinitionStore>(serviceProvider, tenant);
        var builtIns = ActivatorUtilities.CreateInstance<AgentRuntimeBuiltInTools>(serviceProvider, tenant, definitions);
        var mcp = ActivatorUtilities.CreateInstance<ConfigurationMcpCredentialStore>(serviceProvider, tenant);
        var executor = ActivatorUtilities.CreateInstance<CommonAgentExecutor>(serviceProvider,
            tenant, credentials, definitions, builtIns, mcp, new PreparationHttpClients(httpClientFactory));
        var definition = request.Plan.Definition;
        var schema = JsonNode.Parse(definition.Agent.JsonOutput!.Schema.GetRawText())!.AsObject();
        schema["properties"]!["page"]!["enum"] = new JsonArray(request.PageNumber);
        var effective = definition with
        {
            Agent = definition.Agent with
            {
                MaximumOutputTokens = request.Plan.Settings.MaximumOutputTokens,
                JsonOutput = definition.Agent.JsonOutput with { Schema = JsonSerializer.SerializeToElement(schema) },
            },
        };
        var result = await executor.ExecuteAsync(new AgentExecutionContext
        {
            Definition = effective,
            ProviderApiKey = await credentials.GetApiKeyAsync(definition.Agent.Provider, cancellationToken),
            Overrides = new AgentOverrideState { SessionEnabled = false },
            StructuredResponseSink = observer,
            Request = new AgentExecutionRequest
            {
                AgentName = definition.Name,
                SessionId = "document-preparation",
                BypassSession = true,
                BinaryContent = request.Pdf,
                MimeType = "application/pdf",
                Input = JsonSerializer.Serialize(new
                {
                    physical_page = request.PageNumber,
                    original_sha256 = request.OriginalSha256,
                    document_intelligence_draft = request.Draft,
                }),
            },
        }, cancellationToken);
        if (result.Diagnostics?.Error is not null)
            throw new InvalidOperationException("The preparation agent did not complete successfully; retain the paid receipt.");
    }

    private async Task<HttpClient> CreateLayoutHttpClientAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken)
    {
        var credential = runtimeEntraCredential.Credential
            ?? throw new InvalidOperationException("Managed preparation requires the authorized runtime Azure identity.");
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
        var client = httpClientFactory.CreateClient(nameof(DocumentPreparationProvider));
        client.BaseAddress = new Uri(plan.Settings.DocumentIntelligenceEndpoint);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private static AzDocumentLayoutClient Layout(HttpClient client, DocumentPreparationPlan plan) =>
        new(client, new DocumentLayoutOptions
        {
            MaximumPdfBytes = 26_214_400,
            MaximumResponseBytes = 16 * 1024 * 1024,
            MaximumPages = plan.Settings.MaximumPages,
        });

    private sealed class PreparationTenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class PreparationHttpClients(IHttpClientFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient(nameof(DocumentPreparationProvider));
    }
}
