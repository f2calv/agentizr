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
    /// <returns>The process exit code: zero after normal shutdown, or one after an unexpected fault.</returns>
    public static async Task<int> RunAsync(string[] args, Assembly entryAssembly)
    {
        SerilogExtensions.GetBootstrapLogger();

        try
        {
            var (configurationArgs, migrateArgument) = ParseMigrationMode(args);

            // Host builder
            var builder = WebApplication.CreateBuilder(configurationArgs);

            // Configuration
            var (appConfig, applicationMetadata) = builder.InitializeConfiguration(entryAssembly);
            builder.Configuration.AddCommandLine(configurationArgs);
            builder.Services.AddApplicationHostLifecycle(builder.Configuration);
            var selected = ApplyMigrationOverride(
                builder.Configuration.GetSection(FeatureConfig.ConfigurationSectionName)
                    .Get<FeatureConfig>() ?? new FeatureConfig(),
                migrateArgument);
            var features = selected.GetEnabledFeatures();
            var migrateOnly = features.Contains(FeatureNames.Migrate);
            var runtimeEnabled = features.Contains(FeatureNames.Agentizr);

            // Logging
#if DEBUG
            var runtimeLoggingSwitch = runtimeEnabled ? builder.AddAgentRuntimeLogging() : null;
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
            var tenantAuthenticationEnabled = runtimeEnabled && builder.AddAgentRuntimeWebApi();
            if (runtimeEnabled)
                builder.AddAgentRuntimeFeatures(tenantAuthenticationEnabled);
            else
                builder.AddAgentRuntimeDatabase(required: true);
            var administrationEnabled = runtimeEnabled && builder.AddAgentRuntimeAdministration();
#if DEBUG
            if (runtimeEnabled)
                builder.AddAgentRuntimeLoggingManagement();
#endif
            if (runtimeEnabled)
                AddFrontend(builder);
            var mcpEnabled = runtimeEnabled && builder.AddAgentRuntimeMcp(migrateOnly);
            if (features.Contains(FeatureNames.Ingestion))
                builder.AddAgentRuntimeIngestion(runtimeEnabled);
            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IBgFeature)))
                builder.Services.AddFeatureFlagService(features);

            // Build
            await using var app = builder.Build();
            var exitState = app.Services.GetApplicationExitState();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} starting", nameof(AppHost));

            if (migrateOnly)
                await RunDatabaseMigrationAsync(app.Services, CancellationToken.None);
            else
            {
                // Endpoint mapping
                if (runtimeEnabled)
                {
                    app.MapAgentRuntimeEndpoints(tenantAuthenticationEnabled, administrationEnabled);
                    app.MapAgentRuntimeMcp(mcpEnabled);
                    MapFrontend(app, administrationEnabled);
                }
                else
                {
                    app.MapGet("/status", () => Results.Ok(new { Service = "agentizr", Role = FeatureNames.Ingestion }));
                    app.MapHealthChecks("/healthz");
                }

                // Run
                await app.RunAsync();
                return exitState.ExitCode;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not TaskCanceledException)
        {
            Log.Fatal(exception, "{AppName} terminated unexpectedly", AppDomain.CurrentDomain.FriendlyName);
            return 1;
        }
        finally
        {
            Log.Information("Stopped {AppName}", AppDomain.CurrentDomain.FriendlyName);
            await Log.CloseAndFlushAsync();
        }

        return 0;
    }
}
