using CasCap.Common.Extensions;
using CasCap.Common.Models;
using Serilog;

namespace CasCap;

/// <summary>Builds and runs the agentizr application host.</summary>
public static partial class AppHost
{
    /// <summary>Bootstraps and runs the application.</summary>
    /// <param name="args">Command-line arguments forwarded from the entry point.</param>
    public static async Task RunAsync(string[] args)
    {
        var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
        // TODO: Add shared bootstrap logging before CreateBuilder so pre-host failures are captured.
        var builder = WebApplication.CreateBuilder(args);

        var azureAuthConfig = builder.Configuration
            .GetSection(AzureAuthConfig.ConfigurationSectionName)
            .Get<AzureAuthConfig>();
        if (azureAuthConfig?.IsKeyVaultEnabled is true)
        {
            builder.Configuration.AddKeyVaultConfiguration(
                azureAuthConfig.KeyVaultUri,
                azureAuthConfig.TokenCredential,
                new PrefixKeyVaultSecretManager(
                    "AgentRuntime--Agentizr",
                    nameof(CasCap),
                    "AgentRuntime"));
        }

        builder.InitializeSerilog(nameof(Program));

        var appConfig = builder.Configuration
            .GetSection(AppConfig.ConfigurationSectionName)
            .Get<AppConfig>() ?? new AppConfig();
        builder.Services.AddOptionsWithValidateOnStart<AppConfig>()
            .BindConfiguration(AppConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        builder.Services.AddSingleton(TimeProvider.System);

        // TODO: Replace default GitMetadata with deployment-derived values when shared bootstrap supports it.
        var gitMetadata = new GitMetadata();
        builder.InitializeOpenTelemetry(appConfig, gitMetadata);

        var tenantAuthenticationEnabled = AddWebApi(builder);
        AddFeatures(builder, gitMetadata, tenantAuthenticationEnabled);

        var app = builder.Build();

        if (migrateOnly)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<AgentRuntimeDatabaseMigrator>()
                .MigrateAsync(CancellationToken.None);
            return;
        }

        MapEndpoints(app, tenantAuthenticationEnabled);

        await app.RunAsync();
    }
}
