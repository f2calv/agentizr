using CasCap.Common.Extensions;
using CasCap.Constants;
using CasCap.Extensions;
using Serilog;
using System.Reflection;

namespace CasCap;

/// <summary>Builds and runs the agentizr application host.</summary>
public static partial class AppHost
{
    /// <summary>Bootstraps and runs the application.</summary>
    /// <param name="args">Command-line arguments forwarded from the entry point.</param>
    /// <param name="entryAssembly">Entry assembly used for configuration and user-secrets resolution.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(string[] args, Assembly entryAssembly)
    {
        SerilogExtensions.GetBootstrapLogger();

        try
        {
            var migrateArgument = args.Contains("--migrate", StringComparer.Ordinal);
            var configurationArgs = args.Where(arg => arg != "--migrate").ToArray();

            // Host builder
            var builder = WebApplication.CreateBuilder(configurationArgs);

            // Configuration
            var (appConfig, applicationMetadata) = builder.InitializeConfiguration(entryAssembly);
            builder.Configuration.AddCommandLine(configurationArgs);
            var selected = builder.Configuration.GetSection(FeatureConfig.ConfigurationSectionName)
                .Get<FeatureConfig>() ?? new FeatureConfig();
            if (migrateArgument)
                selected = selected with { EnabledFeatures = FeatureNames.Migrate };
            var features = selected.GetEnabledFeatures();
            var migrateOnly = features.Contains(FeatureNames.Migrate);
            var runtimeEnabled = features.Contains(FeatureNames.Agentizr);

            // Logging
#if DEBUG
            var runtimeLoggingSwitch = runtimeEnabled ? AddRuntimeLogging(builder) : null;
            var logger = SerilogWebApplicationBuilderExtensions.InitializeSerilog(builder,
                configureLogger: configuration =>
                {
                    if (runtimeLoggingSwitch is not null)
                        configuration.MinimumLevel.ControlledBy(runtimeLoggingSwitch);
                });
#else
            if (runtimeEnabled && builder.Configuration.GetValue<bool>("CasCap:RuntimeConfigurationConfig:Enabled"))
                throw new InvalidOperationException("The runtime configuration soak requires a Debug build with local source references.");
            var logger = SerilogWebApplicationBuilderExtensions.InitializeSerilog(builder);
#endif

            // Infrastructure
            builder.Services.AddSingleton(TimeProvider.System);

            // Observability
            builder.InitializeOpenTelemetry(appConfig, applicationMetadata);

            // Web API registration
            var tenantAuthenticationEnabled = runtimeEnabled && AddWebApi(builder);
            if (runtimeEnabled)
                AddFeatures(builder, tenantAuthenticationEnabled);
            else
                AddDatabase(builder, required: true);
            var administrationEnabled = runtimeEnabled && AddAdministration(builder);
            if (runtimeEnabled)
                AddFrontend(builder);
            var mcpEnabled = runtimeEnabled && AddMcp(builder, migrateOnly);
            if (features.Contains(FeatureNames.Ingestion))
                AddIngestion(builder, runtimeEnabled);

            // Build
            await using var app = builder.Build();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} starting", nameof(AppHost));

            if (migrateOnly)
            {
                await using var scope = app.Services.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<AgentRuntimeDatabaseMigrator>()
                    .MigrateAsync(CancellationToken.None);
            }
            else
            {
                // Endpoint mapping
                if (runtimeEnabled)
                {
                    MapEndpoints(app, tenantAuthenticationEnabled, administrationEnabled);
                    MapMcp(app, mcpEnabled);
                    MapFrontend(app, administrationEnabled);
                }
                else
                {
                    app.MapGet("/status", () => Results.Ok(new { Service = "agentizr", Role = FeatureNames.Ingestion }));
                    app.MapHealthChecks("/healthz");
                }

                // Run
                await app.RunAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not TaskCanceledException)
        {
            Log.Fatal(exception, "{AppName} terminated unexpectedly", AppDomain.CurrentDomain.FriendlyName);
            throw new InvalidOperationException("Application host terminated unexpectedly.", exception);
        }
        finally
        {
            Log.Information("Stopped {AppName}", AppDomain.CurrentDomain.FriendlyName);
            await Log.CloseAndFlushAsync();
        }

        return 0;
    }
}
