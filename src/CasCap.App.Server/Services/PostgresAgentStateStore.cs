using CasCap.Common.Extensions;
using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;

namespace CasCap.Services;

/// <summary>Owns durable state and connection-bound PostgreSQL session locks, with optional Redis payload caching.</summary>
internal sealed class PostgresAgentStateStore(
    ILogger<PostgresAgentStateStore> logger,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    IOptions<CachingConfig> cachingConfig,
    TimeProvider timeProvider,
    AgentRuntimeMetrics metrics,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    ITenantContext tenantContext,
    IRemoteCache? remoteCache = null) : IAgentSessionLock
{
    private readonly ConcurrentDictionary<string, SessionHandle> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string agentName, string sessionId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var key = AgentStateKey.CreateRedisSessionLock(tenantContext.TenantId, agentName, sessionId);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@key, 0))";
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new AgentSessionConflictException("The session is already being changed.");

            var handle = new SessionHandle(connection, key, _sessions, metrics, tenantContext.TenantId);
            if (!_sessions.TryAdd(key, handle))
            {
                await handle.DisposeAsync();
                throw new AgentSessionConflictException("The session operation has not finished releasing ownership.");
            }
            metrics.RecordHa(tenantContext.TenantId, "lock", "acquired", stopwatch.Elapsed);
            metrics.RecordSessionOwner(tenantContext.TenantId, acquired: true);
            handle.Counted = true;
            return handle;
        }
        catch (Exception error)
        {
            metrics.RecordHa(tenantContext.TenantId, "lock",
                error is AgentSessionConflictException ? "conflict" : "error", stopwatch.Elapsed);
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Checks the durable revision, then reads its immutable cached payload or reloads PostgreSQL.</summary>
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        while (true)
        {
            var revision = await db.AgentStates.AsNoTracking()
                .Where(state => state.Key == key && state.TenantId == tenantContext.TenantId)
                .Select(state => (Guid?)state.Revision).SingleOrDefaultAsync(cancellationToken);
            if (revision is null)
            {
                metrics.RecordHa(tenantContext.TenantId, "state.read", "absent");
                return null;
            }
            var cacheKey = cachingConfig.Value.FormatCacheKey($"{key}:{revision:N}");
            if (remoteCache is not null)
            {
                try
                {
                    var cached = await remoteCache.Db.StringGetAsync(cacheKey).WaitAsync(cancellationToken);
                    if (cached.HasValue)
                    {
                        metrics.RecordHa(tenantContext.TenantId, "cache.read", "hit");
                        return ((string)cached!).FromJson<T>();
                    }
                    metrics.RecordHa(tenantContext.TenantId, "cache.read", "miss");
                }
                catch (RedisException)
                {
                    LogCacheUnavailable();
                    metrics.RecordHa(tenantContext.TenantId, "cache.read", "error");
                }
            }
            var payload = await db.AgentStates.AsNoTracking()
                .Where(state => state.Key == key && state.TenantId == tenantContext.TenantId && state.Revision == revision)
                .Select(state => state.PayloadJson).SingleOrDefaultAsync(cancellationToken);
            if (payload is null)
            {
                metrics.RecordHa(tenantContext.TenantId, "state.read", "revision_changed");
                continue; // A concurrent status read crossed a write or delete; resolve the new revision.
            }
            metrics.RecordHa(tenantContext.TenantId, "state.read", "postgres");
            if (remoteCache is not null)
            {
                try
                {
                    await remoteCache.Db.StringSetAsync(
                        cacheKey, payload, TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours))
                        .WaitAsync(cancellationToken);
                    metrics.RecordHa(tenantContext.TenantId, "cache.fill", "succeeded");
                }
                catch (RedisException)
                {
                    LogCacheUnavailable();
                    metrics.RecordHa(tenantContext.TenantId, "cache.fill", "error");
                }
            }
            return payload.FromJson<T>();
        }
    }

    /// <summary>Writes durably using the exact connection that owns the session lock; never reconnects a lost owner.</summary>
    public async ValueTask WriteAsync<T>(
        string agentName, string sessionId, string key, T? value, CancellationToken cancellationToken) where T : class
    {
        var lockKey = AgentStateKey.CreateRedisSessionLock(tenantContext.TenantId, agentName, sessionId);
        if (!_sessions.TryGetValue(lockKey, out var handle) || handle.Connection.State != ConnectionState.Open)
        {
            metrics.RecordHa(tenantContext.TenantId, "state.write", "ownership_lost");
            throw new AgentSessionConflictException("Session ownership is required before changing runtime state.");
        }

        await using var command = handle.Connection.CreateCommand();
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("tenant", tenantContext.TenantId);
        if (value is null)
            command.CommandText = "DELETE FROM agent_runtime.agent_states WHERE key = @key AND tenant_id = @tenant";
        else
        {
            command.CommandText = """
                INSERT INTO agent_runtime.agent_states (key, tenant_id, agent_name, session_id, revision, payload_json, updated_at_utc)
                VALUES (@key, @tenant, @agent, @session, @revision, CAST(@payload AS jsonb), @updated)
                ON CONFLICT (key) DO UPDATE SET revision = EXCLUDED.revision,
                    payload_json = EXCLUDED.payload_json, updated_at_utc = EXCLUDED.updated_at_utc
                """;
            command.Parameters.AddWithValue("agent", agentName);
            command.Parameters.AddWithValue("session", sessionId);
            command.Parameters.AddWithValue("revision", Guid.NewGuid());
            command.Parameters.AddWithValue("payload", value.ToJson());
            command.Parameters.AddWithValue("updated", timeProvider.GetUtcNow());
        }
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            metrics.RecordHa(tenantContext.TenantId, value is null ? "state.delete" : "state.write", "succeeded", stopwatch.Elapsed);
        }
        catch
        {
            metrics.RecordHa(tenantContext.TenantId, value is null ? "state.delete" : "state.write", "error", stopwatch.Elapsed);
            throw;
        }
    }

    private void LogCacheUnavailable()
    {
        if (logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning("{ClassName} Redis payload cache unavailable; using PostgreSQL", nameof(PostgresAgentStateStore));
    }

    private sealed class SessionHandle(
        NpgsqlConnection connection,
        string key,
        ConcurrentDictionary<string, SessionHandle> sessions,
        AgentRuntimeMetrics metrics,
        string tenant) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        private int _disposed;
        public bool Counted { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                if (Connection.State == ConnectionState.Open)
                {
                    await using var command = Connection.CreateCommand();
                    command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@key, 0))";
                    command.Parameters.AddWithValue("key", key);
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                sessions.TryRemove(new KeyValuePair<string, SessionHandle>(key, this));
                if (Counted)
                    metrics.RecordSessionOwner(tenant, acquired: false);
                await Connection.DisposeAsync();
            }
        }
    }
}
