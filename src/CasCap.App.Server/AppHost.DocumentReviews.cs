using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasCap;

public static partial class AppHost
{
    private static void AddDocumentReviews(WebApplicationBuilder builder, bool databaseEnabled)
    {
#if DEBUG
        builder.Services.TryAddSingleton<DocumentChunkingService>();
        builder.Services.AddScoped(services => new DocumentCatalogueAdministrationService(
            services.GetRequiredService<ITenantContext>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>()));
        builder.Services.AddScoped(services => new DocumentReviewAdministrationService(
            services.GetRequiredService<IOptions<DocumentPreparationConfig>>(),
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<DocumentChunkingService>(),
            services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AgentRuntimeDbContext>>(),
            services.GetService<DocumentPreparationMarkdownStore>()));
        if (!databaseEnabled)
            return;
        builder.Services.AddOptionsWithValidateOnStart<DocumentPreparationConfig>()
            .BindConfiguration(DocumentPreparationConfig.ConfigurationSectionName).ValidateDataAnnotations();
        builder.Services.TryAddScoped<DocumentPreparationMarkdownStore>();
#endif
    }
}
