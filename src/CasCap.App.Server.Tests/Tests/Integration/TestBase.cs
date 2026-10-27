#if DEBUG
using Azure;
using Azure.Identity;
using CasCap.Common.Extensions;
using CasCap.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CasCap.Tests.Integration;

/// <summary>Owns opt-in live-document configuration without starting the application or background workers.</summary>
public abstract class TestBase : IAsyncDisposable
{
    /// <summary>Gets safe test diagnostics.</summary>
    protected readonly ITestOutputHelper _output;
    /// <summary>Gets the standard test configuration and explicitly selected private overlay.</summary>
    protected readonly ConfigurationManager _configuration = new();
    private ServiceProvider? _serviceProvider;

    /// <summary>Loads standard providers; a private overlay is optional and never copied into build output.</summary>
    protected TestBase(ITestOutputHelper output)
    {
        _output = output;
        _configuration.AddStandardConfiguration("Test", typeof(TestBase).Assembly);
        var localPath = _configuration["DocumentQuality:LocalConfigurationPath"];
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            if (!Path.IsPathFullyQualified(localPath))
                throw new InvalidOperationException("DocumentQuality:LocalConfigurationPath must be absolute.");
            _configuration.AddJsonFile(localPath, optional: false, reloadOnChange: false)
                .AddUserSecrets(typeof(TestBase).Assembly, optional: true)
                .AddEnvironmentVariables();
        }
    }

    /// <summary>Adds the application's exact Key Vault provider and identity after explicit live opt-in.</summary>
    protected ServiceProvider InitializeLiveServices()
    {
        Assert.SkipUnless(_configuration.GetValue<bool>("DocumentQuality:LiveEnabled"),
            "Set DocumentQuality__LiveEnabled=true to authorize read-only live document tests.");
        try
        {
            var credential = _configuration.AddRuntimeKeyVaultConfiguration();
            Assert.True(credential.Credential is not null, "Configure the runtime's certificate or workload identity.");
            var services = new ServiceCollection().AddSingleton<IConfiguration>(_configuration)
                .AddSingleton(credential).AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            services.AddSingleton<ILoggerProvider>(_ => new TestLogProvider(_output));
            services.AddOptions<DocumentIngestionConfig>().BindConfiguration(DocumentIngestionConfig.ConfigurationSectionName)
                .PostConfigure(options =>
                {
                    for (var index = 0; index < options.Sources.Count; index++)
                        options.Sources[index] = options.Sources[index] with
                        {
                            TenantId = $"quality-tenant-{index}",
                            CollectionId = "quality-reference",
                        };
                });
            services.AddOptions<DocumentPreparationConfig>().BindConfiguration(DocumentPreparationConfig.ConfigurationSectionName)
                .ValidateDataAnnotations();
            services.AddSingleton<DocumentBlobDownloadService>();
            services.AddSingleton<DocumentChunkingService>();
            services.AddTransient<PdfDocumentPreparationService>();
            _serviceProvider = services.BuildServiceProvider();
            return _serviceProvider;
        }
        catch (RequestFailedException exception)
        {
            throw new InvalidOperationException($"Key Vault configuration failed with HTTP status {exception.Status}; details withheld.");
        }
        catch (AuthenticationFailedException)
        {
            throw new InvalidOperationException("Key Vault identity authentication failed; details withheld.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
        _configuration.Dispose();
        GC.SuppressFinalize(this);
    }
}
#endif
