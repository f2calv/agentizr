using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasCap;

public static partial class AppHost
{
    private static void AddDocumentReviews(WebApplicationBuilder builder, bool databaseEnabled)
    {
#if DEBUG
        AddDocumentIngestionConfiguration(builder);
        builder.Services.AddOptionsWithValidateOnStart<DocumentIngestionConfig>()
            .Validate(options => !options.SynchronizationEnabled
                || Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true),
                "Enabled document synchronization requires valid ingestion source configuration.");
        builder.Services.TryAddSingleton<DocumentChunkingService>();
        AddDocumentIndexingConfiguration(builder);
        builder.Services.AddScoped(services => new DocumentIndexingAdministrationService(
            services.GetRequiredService<DocumentReviewAdministrationService>(),
            services.GetRequiredService<IActorContext>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>() is null
                ? null : services.GetRequiredService<DocumentIndexingStore>()));
        builder.Services.AddScoped(services => new DocumentSynchronizationAdministrationService(
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<IOptions<DocumentIngestionConfig>>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>()));
        builder.Services.AddScoped(services => new DocumentCatalogueAdministrationService(
            services.GetRequiredService<ITenantContext>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>()));
        builder.Services.AddScoped(services => new DocumentReviewAdministrationService(
            services.GetRequiredService<IOptions<DocumentPreparationConfig>>(),
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<DocumentChunkingService>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>(),
            services.GetService<DocumentPreparationMarkdownStore>(),
            services.GetRequiredService<IActorContext>(),
            services.GetRequiredService<IOptions<DocumentIndexingConfig>>()));
        if (!databaseEnabled)
            return;
        builder.Services.AddOptionsWithValidateOnStart<DocumentPreparationConfig>()
            .BindConfiguration(DocumentPreparationConfig.ConfigurationSectionName).ValidateDataAnnotations();
        builder.Services.TryAddScoped<DocumentPreparationMarkdownStore>();
        builder.Services.TryAddScoped<DocumentIndexingStore>();
#endif
    }
}
