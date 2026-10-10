using CasCap.Common.Extensions;
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
            var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);

            // Host builder
            var builder = WebApplication.CreateBuilder(args);

            // Configuration
            var (appConfig, gitMetadata) = builder.InitializeConfiguration(entryAssembly);

            // Logging
            var logger = SerilogWebApplicationBuilderExtensions.InitializeSerilog(builder);

            // Infrastructure
            builder.Services.AddSingleton(TimeProvider.System);

            // Observability
            builder.InitializeOpenTelemetry(appConfig, gitMetadata);

            // Web API registration
            var tenantAuthenticationEnabled = AddWebApi(builder);

            // Feature registration
            AddFeatures(builder, tenantAuthenticationEnabled);

            // Build
            var app = builder.Build();

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
                MapEndpoints(app, tenantAuthenticationEnabled);

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
