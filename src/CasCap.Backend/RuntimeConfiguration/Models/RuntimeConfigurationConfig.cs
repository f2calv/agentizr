namespace CasCap.RuntimeConfiguration.Models;

/// <summary>Immutable, opt-in bootstrap settings for the Debug runtime-logging soak.</summary>
public sealed class RuntimeConfigurationConfig : IValidatableObject
{
    /// <summary>Gets whether this host participates in the runtime-configuration soak.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the durable application scope, required when enabled.</summary>
    [Required, MaxLength(100)]
    public string Application { get; init; } = string.Empty;

    /// <summary>Gets the PostgreSQL connection, which may target a standalone or existing database.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Gets whether to reuse the existing application account and effective database, including its database-name override.</summary>
    public bool ApplicationDatabaseReuseEnabled { get; init; }

    /// <summary>Gets the polling interval in milliseconds.</summary>
    [Range(100, 3_600_000)]
    public int RefreshIntervalMs { get; init; } = 5_000;

    /// <summary>Gets whether the Debug host exposes the authorized logging editor API.</summary>
    public bool ManagementEnabled { get; init; }

    /// <summary>Gets explicit host-wide operator identifiers in the administration identity claim.</summary>
    public string[] ManagementOperators { get; init; } = [];

    /// <summary>Gets whether the operator explicitly grants existing app administrators host-wide logging rights.</summary>
    /// <remarks>Disabled by default; tenant administration alone never grants these rights.</remarks>
    public bool ManagementUseAppAdministratorAssignments { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApplicationDatabaseReuseEnabled == !string.IsNullOrWhiteSpace(ConnectionString))
            yield return new("Select either application-database reuse or an explicit runtime connection, but not both.",
                [nameof(ApplicationDatabaseReuseEnabled), nameof(ConnectionString)]);
        if (ManagementEnabled && (ManagementOperators.Any(string.IsNullOrWhiteSpace)
            || (ManagementUseAppAdministratorAssignments ? ManagementOperators.Length != 0 : ManagementOperators.Length == 0)))
            yield return new("Management requires either explicit operators or the explicit app-administrator grant, not both.",
                [nameof(ManagementOperators), nameof(ManagementUseAppAdministratorAssignments)]);
    }
}
