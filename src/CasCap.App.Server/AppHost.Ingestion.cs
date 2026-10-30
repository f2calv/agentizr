using CasCap.Common.Extensions;
using CasCap.Constants;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasCap;

/// <summary>Registers the elected ingestion role independently from runtime endpoints.</summary>
public static partial class AppHost
{
    internal static void AddIngestion(WebApplicationBuilder builder, bool runtimeEnabled)
    {
#if DEBUG
        AddDocumentIngestionConfiguration(builder);
        AddDocumentIndexingConfiguration(builder);
        var database = builder.Configuration.GetSection(DatabaseConfig.ConfigurationSectionName).Get<DatabaseConfig>();
        var caching = builder.Configuration.GetSection(CachingConfig.ConfigurationSectionName).Get<CachingConfig>();
        if (string.IsNullOrWhiteSpace(database?.ConnectionString))
            throw new InvalidOperationException("Ingestion requires PostgreSQL.");
        if (caching is null || !caching.RemoteCache.IsEnabled || !caching.DistributedLockingEnabled
            || string.IsNullOrWhiteSpace(caching.RemoteCacheConnectionString))
            throw new InvalidOperationException("Ingestion requires shared Redis with distributed locking enabled.");
        if (!runtimeEnabled)
        {
            builder.Services.AddCasCapCaching(builder.Configuration);
            builder.Services.AddOptionsWithValidateOnStart<AgentRuntimeConfig>()
                .BindConfiguration(AgentRuntimeConfig.ConfigurationSectionName).ValidateDataAnnotations();
            builder.Services.AddOptionsWithValidateOnStart<AgentDefinitionPolicyConfig>()
                .BindConfiguration(AgentDefinitionPolicyConfig.ConfigurationSectionName).ValidateDataAnnotations();
            builder.Services.AddOptions<ApiAuthConfig>().BindConfiguration(ApiAuthConfig.ConfigurationSectionName);
        }
        builder.Services.AddHealthChecks();
        builder.Services.AddOptionsWithValidateOnStart<DocumentIngestionConfig>()
            .ValidateDataAnnotations()
            .Validate<IServiceProviderIsService>((options, services) =>
                !options.ProcessingEnabled || services.IsService(typeof(IDocumentProcessor)),
                "Document processing requires a real registered document processor.");
        builder.Services.AddOptionsWithValidateOnStart<DocumentPreparationConfig>()
            .BindConfiguration(DocumentPreparationConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        builder.Services.AddSingleton<DocumentChunkingService>();
        builder.Services.AddScoped<PdfDocumentPreparationService>();
        builder.Services.AddSingleton<DocumentBlobDownloadService>();
        builder.Services.AddScoped<DocumentPreparationStoreService>();
        builder.Services.AddScoped<DocumentPreparationMarkdownStore>();
        builder.Services.AddScoped<DocumentPreparationOperationStore>();
        builder.Services.AddScoped<DocumentPreparationModelVerifier>();
        builder.Services.AddScoped<IDocumentPreparationProvider, DocumentPreparationProvider>();
        builder.Services.AddScoped<IDocumentEmbeddingProvider, DocumentEmbeddingProvider>();
        builder.Services.AddScoped<DocumentOllamaEmbeddingProvider>();
        builder.Services.TryAddScoped<DocumentIndexingStore>();
        builder.Services.AddScoped<DocumentIndexingProcessingService>();
        builder.Services.AddHttpClient(nameof(DocumentEmbeddingProvider))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped<HybridDocumentProcessor>();
        builder.Services.AddScoped<PdfDocumentProcessor>();
        builder.Services.TryAddScoped<IDocumentProcessor, DocumentPreparationProcessor>();
        builder.Services.AddHttpClient(nameof(DocumentPreparationProvider))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddHttpClient(nameof(DocumentPreparationModelVerifier))
            .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton<IDocumentIngestionEventQueue, AzureDocumentIngestionQueue>();
        builder.Services.AddSingleton<IDocumentBlobInventory, AzureDocumentBlobInventory>();
        builder.Services.AddScoped<DocumentSynchronizationService>();
        builder.Services.AddScoped<IDocumentIngestionInbox, DocumentIngestionInboxService>();
        builder.Services.AddScoped<DocumentIngestionAdmissionService>();
        builder.Services.AddScoped<DocumentIngestionJobService>();
        builder.Services.AddScoped<DocumentIngestionProcessingService>();
        builder.Services.AddSingleton<IBgFeature, DocumentIngestionFeature>();
        builder.Services.AddFeatureFlagService(new HashSet<string>([FeatureNames.Ingestion], StringComparer.OrdinalIgnoreCase));
#else
        throw new InvalidOperationException("The ingestion PoC requires Debug source references until its shared APIs are published.");
#endif
    }

#if DEBUG
    internal static void AddDocumentIngestionConfiguration(WebApplicationBuilder builder) =>
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DocumentIngestionConfig>>(
            new ConfigureFromConfigurationOptions<DocumentIngestionConfig>(
                builder.Configuration.GetSection(DocumentIngestionConfig.ConfigurationSectionName))));

    private static void AddDocumentIndexingConfiguration(WebApplicationBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DocumentIndexingConfig>>(
            new ConfigureFromConfigurationOptions<DocumentIndexingConfig>(
                builder.Configuration.GetSection(DocumentIndexingConfig.ConfigurationSectionName))));
        builder.Services.AddOptionsWithValidateOnStart<DocumentIndexingConfig>().ValidateDataAnnotations();
    }
#endif
}
