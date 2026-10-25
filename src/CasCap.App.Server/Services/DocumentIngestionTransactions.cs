using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Shares the PoC admission/job mutation lock; callers own the surrounding transaction.</summary>
internal static class DocumentIngestionTransactions
{
    /// <summary>Serializes every admission, claim and outcome mutation across PostgreSQL replicas.</summary>
    public static async Task LockAsync(AgentRuntimeDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"agent_runtime:document_ingestion:admission"}, 0))",
                cancellationToken);
    }

    /// <summary>Uses database time for PostgreSQL deadlines and the injected clock in isolated SQLite tests.</summary>
    public static Task<DateTimeOffset> GetUtcNowAsync(
        AgentRuntimeDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    ) =>
        db.Database.IsNpgsql()
            ? db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken)
            : Task.FromResult(timeProvider.GetUtcNow());
}
