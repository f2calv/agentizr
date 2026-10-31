using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CasCap.Extensions;

/// <summary>Registers shared persistence independently of web and worker roles.</summary>
public static partial class WebApplicationBuilderExtensions
{
    /// <summary>Registers the runtime database factory and finite migration service.</summary>
    /// <param name="builder">Application builder.</param>
    /// <param name="required">Whether missing PostgreSQL configuration prevents startup.</param>
    /// <returns>Whether persistence is configured.</returns>
    public static bool AddAgentRuntimeDatabase(this WebApplicationBuilder builder, bool required)
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

        builder.Services.AddDbContextFactory<AgentRuntimeDbContext>(options => options.UseNpgsql(EffectiveDatabaseConnectionString(config)));
        builder.Services.AddScoped<AgentRuntimeDatabaseMigrator>();
        return true;
    }

    /// <summary>Resolves the shared application target without losing its database-name override.</summary>
    internal static string EffectiveDatabaseConnectionString(DatabaseConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
            throw new InvalidOperationException("The selected application database connection is required.");
        var connection = new NpgsqlConnectionStringBuilder(config.ConnectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        };
        if (!string.IsNullOrWhiteSpace(config.DatabaseName))
            connection.Database = config.DatabaseName;
        return connection.ConnectionString;
    }
}
