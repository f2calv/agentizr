using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasCap.Extensions;

public static partial class WebApplicationBuilderExtensions
{
    private static void AddDocumentKnowledge(WebApplicationBuilder builder, bool databaseEnabled)
    {
        builder.Services.AddOptionsWithValidateOnStart<DocumentKnowledgeConfig>()
            .BindConfiguration(DocumentKnowledgeConfig.ConfigurationSectionName).ValidateDataAnnotations()
            .Validate(options => !options.Enabled || databaseEnabled, "Knowledge retrieval requires PostgreSQL.")
            .Validate<IOptions<TenantAuthenticationConfig>>((options, auth) => !options.Enabled || auth.Value.Enabled,
                "Knowledge retrieval requires validated bearer tenant authentication, including in Development.")
            .Validate<IOptions<DocumentIndexingConfig>>((options, indexing) => !options.Enabled
                || indexing.Value.Enabled && options.Bindings.All(binding => indexing.Value.Collections.Any(collection =>
                    collection.TenantId == binding.TenantId && collection.CollectionId == binding.CollectionId
                    && indexing.Value.Profiles.Any(profile => profile.Id == collection.ProfileId
                        && profile.Azure is not null && profile.HasExecutionBinding))),
                "Every knowledge binding requires an existing collection and one executable Azure indexing profile.");
        AddDocumentEmbeddingTransport(builder);
        if (databaseEnabled)
            builder.Services.TryAddScoped<DocumentKnowledgeStore>();
        builder.Services.AddScoped(services => new DocumentKnowledgeService(
            services.GetRequiredService<ILogger<DocumentKnowledgeService>>(),
            services.GetRequiredService<IOptions<DocumentKnowledgeConfig>>(),
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<IAgentDefinitionStore>(),
            services.GetService<IDbContextFactory<AgentRuntimeDbContext>>() is null
                ? null : services.GetRequiredService<DocumentKnowledgeStore>(),
            services.GetRequiredService<DocumentEmbeddingProvider>(),
            services.GetRequiredService<DocumentChunkingService>()));
    }

    private static void AddDocumentEmbeddingTransport(WebApplicationBuilder builder)
    {
        builder.Services.TryAddScoped<DocumentEmbeddingProvider>();
        builder.Services.TryAddScoped<IDocumentEmbeddingProvider>(services => services.GetRequiredService<DocumentEmbeddingProvider>());
        builder.Services.TryAddScoped<DocumentOllamaEmbeddingProvider>();
        builder.Services.TryAddScoped<DocumentLlamaCppEmbeddingProvider>();
        builder.Services.TryAddScoped<DocumentPreparationModelVerifier>();
        builder.Services.AddHttpClient(nameof(DocumentEmbeddingProvider))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddHttpClient(nameof(DocumentPreparationModelVerifier))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }
}
