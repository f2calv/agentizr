using CasCap.Common.Extensions;
using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Data;

namespace CasCap.Services;

/// <summary>Owns durable state and connection-bound PostgreSQL session locks, with optional Redis payload caching.</summary>
internal sealed class PostgresAgentStateStore(
    ILogger<PostgresAgentStateStore> logger,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    IOptions<CachingConfig> cachingConfig,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    ITenantContext tenantContext,
    IRemoteCache? remoteCache = null) : IAgentSessionLock
{
    private readonly ConcurrentDictionary<string, SessionHandle> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string agentName, string sessionId, CancellationToken cancellationToken)
    {
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

            var handle = new SessionHandle(connection, key, _sessions);
            if (!_sessions.TryAdd(key, handle))
            {
                await handle.DisposeAsync();
                throw new AgentSessionConflictException("The session operation has not finished releasing ownership.");
            }
            return handle;
        }
        catch
        {
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
                return null;
            var cacheKey = cachingConfig.Value.FormatCacheKey($"{key}:{revision:N}");
            if (remoteCache is not null)
            {
                try
                {
                    var cached = await remoteCache.Db.StringGetAsync(cacheKey).WaitAsync(cancellationToken);
                    if (cached.HasValue)
                        return ((string)cached!).FromJson<T>();
                }
                catch (RedisException)
                {
                    LogCacheUnavailable();
                }
            }
            var payload = await db.AgentStates.AsNoTracking()
                .Where(state => state.Key == key && state.TenantId == tenantContext.TenantId && state.Revision == revision)
                .Select(state => state.PayloadJson).SingleOrDefaultAsync(cancellationToken);
            if (payload is null)
                continue; // A concurrent status read crossed a write or delete; resolve the new revision.
            if (remoteCache is not null)
            {
                try
                {
                    await remoteCache.Db.StringSetAsync(
                        cacheKey, payload, TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours))
                        .WaitAsync(cancellationToken);
                }
                catch (RedisException)
                {
                    LogCacheUnavailable();
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
            throw new AgentSessionConflictException("Session ownership is required before changing runtime state.");

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
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void LogCacheUnavailable()
    {
        if (logger.IsEnabled(LogLevel.Warning))
            logger.LogWarning("{ClassName} Redis payload cache unavailable; using PostgreSQL", nameof(PostgresAgentStateStore));
    }

    private sealed class SessionHandle(
        NpgsqlConnection connection,
        string key,
        ConcurrentDictionary<string, SessionHandle> sessions) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        private int _disposed;

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
                await Connection.DisposeAsync();
            }
        }
    }
}
