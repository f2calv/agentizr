using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CasCap;

/// <summary>Creates the definition DbContext for EF Core design-time tooling.</summary>
public sealed class AgentRuntimeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AgentRuntimeDbContext>
{
    /// <inheritdoc/>
    public AgentRuntimeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CasCap__AgentRuntimeDatabaseConfig__ConnectionString")
            ?? "Host=localhost;Database=agentizr_design;Username=postgres;Password=not-used";
        var options = new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AgentRuntimeDbContext(options);
    }
}
