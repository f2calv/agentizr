using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CasCap;

/// <summary>Registers shared persistence independently of web and worker roles.</summary>
public static partial class AppHost
{
    private static bool AddDatabase(WebApplicationBuilder builder, bool required)
    {
        builder.Services.AddOptionsWithValidateOnStart<DatabaseConfig>()
            .BindConfiguration(DatabaseConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        var config = builder.Configuration.GetSection(DatabaseConfig.ConfigurationSectionName)
            .Get<DatabaseConfig>() ?? new DatabaseConfig();
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            if (required)
                throw new InvalidOperationException("PostgreSQL Agent Runtime definitions are required for the selected role.");
            return false;
        }

        var connection = new NpgsqlConnectionStringBuilder(config.ConnectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        };
        if (!string.IsNullOrWhiteSpace(config.DatabaseName))
            connection.Database = config.DatabaseName;
        builder.Services.AddDbContextFactory<AgentRuntimeDbContext>(options => options.UseNpgsql(connection.ConnectionString));
        builder.Services.AddScoped<AgentRuntimeDatabaseMigrator>();
        return true;
    }
}
