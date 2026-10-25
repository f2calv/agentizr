using CasCap.Common.Extensions;
using CasCap.Constants;

namespace CasCap;

/// <summary>Registers the elected ingestion role independently from runtime endpoints.</summary>
public static partial class AppHost
{
    internal static void AddIngestion(WebApplicationBuilder builder, bool runtimeEnabled)
    {
#if DEBUG
        var database = builder.Configuration.GetSection(DatabaseConfig.ConfigurationSectionName).Get<DatabaseConfig>();
        var caching = builder.Configuration.GetSection(CachingConfig.ConfigurationSectionName).Get<CachingConfig>();
        if (string.IsNullOrWhiteSpace(database?.ConnectionString))
            throw new InvalidOperationException("Ingestion requires PostgreSQL.");
        if (caching is null || !caching.RemoteCache.IsEnabled || !caching.DistributedLockingEnabled
            || string.IsNullOrWhiteSpace(caching.RemoteCacheConnectionString))
            throw new InvalidOperationException("Ingestion requires shared Redis with distributed locking enabled.");
        if (!runtimeEnabled)
            builder.Services.AddCasCapCaching(builder.Configuration);
        builder.Services.AddHealthChecks();
        builder.Services.AddOptionsWithValidateOnStart<DocumentIngestionConfig>()
            .BindConfiguration(DocumentIngestionConfig.ConfigurationSectionName)
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
        builder.Services.AddSingleton<IDocumentIngestionEventQueue, AzureDocumentIngestionQueue>();
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
}
