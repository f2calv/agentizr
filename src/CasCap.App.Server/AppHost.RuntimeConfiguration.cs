using CasCap.Common.Configuration.PostgreSql.Extensions;
using CasCap.Common.Configuration.Runtime.Models;
using CasCap.RuntimeConfiguration.Services;
using Serilog.Core;
using Serilog.Events;

namespace CasCap;

/// <summary>Registers the deliberately narrow, Debug-only runtime logging soak.</summary>
public static partial class AppHost
{
    internal static LoggingLevelSwitch? AddRuntimeLogging(WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("CasCap:RuntimeConfigurationConfig")
            .Get<RuntimeConfigurationConfig>() ?? new RuntimeConfigurationConfig();
        if (!settings.Enabled)
            return null;
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        var minimumLevel = builder.Configuration.GetValue<LogEventLevel?>("Serilog:MinimumLevel:Default")
            ?? builder.Configuration.GetValue<LogEventLevel?>("Serilog:MinimumLevel")
            ?? LogEventLevel.Information;
        if (!Enum.IsDefined(minimumLevel))
            throw new InvalidOperationException("The deployment logging minimum must be a defined Serilog level.");
        var database = settings.ApplicationDatabaseReuseEnabled
            ? builder.Configuration.GetSection(DatabaseConfig.ConfigurationSectionName).Get<DatabaseConfig>() ?? new()
            : new DatabaseConfig { ConnectionString = settings.ConnectionString };
        builder.Services.AddPostgresRuntimeConfiguration(
            new RuntimeConfigurationOptions
            {
                Scope = new RuntimeConfigurationScope(settings.Application, builder.Environment.EnvironmentName),
                ContractId = "logging",
                RefreshInterval = TimeSpan.FromMilliseconds(settings.RefreshIntervalMs),
            }, EffectiveDatabaseConnectionString(database), new RuntimeLoggingControls { MinimumLevel = minimumLevel });
        var levelSwitch = new LoggingLevelSwitch(minimumLevel);
        builder.Services.AddSingleton(levelSwitch);
        builder.Services.AddSingleton<RuntimeLoggingProbe>();
        builder.Services.AddHostedService<RuntimeLoggingService>();
        return levelSwitch;
    }
}
