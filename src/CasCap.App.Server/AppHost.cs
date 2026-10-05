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
        // TODO: Add shared bootstrap logging before CreateBuilder so pre-host failures are captured.
        var builder = WebApplication.CreateBuilder(args);

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

        AddFeatures(builder, gitMetadata);
        AddWebApi(builder);

        var app = builder.Build();

        MapEndpoints(app);

        await app.RunAsync();
    }
}
