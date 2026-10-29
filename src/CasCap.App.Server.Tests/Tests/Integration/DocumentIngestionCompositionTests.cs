#if DEBUG
using CasCap.Common.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the real ingestion registration and startup options without starting any transport.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "Ingestion")]
public sealed class DocumentIngestionCompositionTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    public void ProcessorGate(bool enabled, bool registered, bool valid)
    {
        var builder = WebApplication.CreateBuilder();
        using var configuration = builder.Configuration;
        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseConfig.ConfigurationSectionName}:ConnectionString"] = "Host=database.example.invalid;Database=example;Username=example",
            [$"{CachingConfig.ConfigurationSectionName}:RemoteCache:IsEnabled"] = "true",
            [$"{CachingConfig.ConfigurationSectionName}:DistributedLockingEnabled"] = "true",
            [$"{CachingConfig.ConfigurationSectionName}:RemoteCacheConnectionString"] = "cache.example.invalid",
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:LeadershipResource"] = DocumentIngestionTestData.Config.LeadershipResource,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:QueueUri"] = DocumentIngestionTestData.Config.QueueUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:PoisonQueueUri"] = DocumentIngestionTestData.Config.PoisonQueueUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:Topic"] = DocumentIngestionTestData.Config.Sources[0].Topic,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:ContainerUri"] = DocumentIngestionTestData.Config.Sources[0].ContainerUri,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:TenantId"] = DocumentIngestionTestData.Config.Sources[0].TenantId,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:Sources:0:CollectionId"] = DocumentIngestionTestData.Config.Sources[0].CollectionId,
            [$"{DocumentIngestionConfig.ConfigurationSectionName}:ProcessingEnabled"] = enabled.ToString(),
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new RuntimeEntraCredential(() => null));
        builder.Services.AddSingleton<ILeadershipRunner, UnusedRunner>();
        builder.Services.AddDbContextFactory<AgentRuntimeDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        if (registered)
            builder.Services.AddScoped<IDocumentProcessor, UnusedProcessor>();
        AppHost.AddDocumentIngestionConfiguration(builder);
        AppHost.AddIngestion(builder, runtimeEnabled: true);
        if (!valid)
            builder.Services.RemoveAll<IDocumentProcessor>();
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var options = provider.GetRequiredService<IOptions<DocumentIngestionConfig>>();
        if (valid)
        {
            Assert.Equal(enabled, options.Value.ProcessingEnabled);
            Assert.Single(options.Value.Sources);
            using var scope = provider.CreateScope();
            Assert.IsType(registered ? typeof(UnusedProcessor) : typeof(DocumentPreparationProcessor),
                Assert.Single(scope.ServiceProvider.GetServices<IDocumentProcessor>()));
        }
        else
        {
            var exception = Assert.Throws<OptionsValidationException>(() => options.Value);
            Assert.Contains("Document processing requires a real registered document processor.", exception.Failures);
        }
    }

    private sealed class UnusedProcessor : IDocumentProcessor
    {
        public Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedRunner : ILeadershipRunner
    {
        public Task RunAsync(string resource, Func<CancellationToken, Task> runAsync, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
#endif
