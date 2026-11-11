using CasCap.AgentRuntime.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Data.Common;

namespace CasCap.Services;

/// <summary>
/// Controls this instance's realtime calls through a shared leased call record: renews the leases of its own calls,
/// hangs up calls another replica asked to end, sweeps calls whose owner's lease expired and ends its own calls when
/// the host stops.
/// </summary>
internal sealed class RealtimeCallRegistry(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    IRealtimeCallStore store,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RealtimeCallRegistry> logger) : BackgroundService
{
    /// <summary>How often the owner renews its leases and checks for end requests.</summary>
    internal static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a lease survives without renewal, tolerating a few missed renewals.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(20);

    // Long enough for a client to learn why the runtime ended a call after its media dropped.
    private static readonly TimeSpan EndedRetention = TimeSpan.FromMinutes(10);

    // Covers an expired lease, the next sweep and the provider hang-up when the owner is gone.
    private static readonly TimeSpan RemoteEndWait = LeaseDuration + RenewInterval + TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RemoteEndPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HangUpTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, RealtimeCallSession> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _completions = new(StringComparer.Ordinal);
    private readonly Lock _admission = new();

    /// <summary>Gets this instance's identity in call records.</summary>
    public string OwnerId { get; } = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>Gets whether a new call could currently be admitted.</summary>
    public bool HasCapacity => _calls.Count < runtimeConfig.Value.MaximumRealtimeCallsPerInstance;

    /// <summary>
    /// Records and starts controlling a call unless this instance is at capacity; the caller hangs up a call that
    /// was not admitted or could not be recorded.
    /// </summary>
    public async Task<bool> TryStartAsync(RealtimeCallSession call, CancellationToken cancellationToken)
    {
        lock (_admission)
        {
            if (_calls.Count >= runtimeConfig.Value.MaximumRealtimeCallsPerInstance || !_calls.TryAdd(call.CallId, call))
                return false;
        }
        var now = timeProvider.GetUtcNow();
        var options = call.Options;
        try
        {
            await store.AddAsync(new RealtimeCallRecord
            {
                CallId = call.CallId,
                TenantId = call.TenantId,
                AgentName = call.AgentName,
                OwnerId = OwnerId,
                Status = RealtimeCallStatus.Active,
                ProviderName = options.ProviderName,
                Endpoint = options.Endpoint,
                ProviderCallId = options.ProviderCallId,
                StartedAt = now,
                LeaseExpiresAt = now + LeaseDuration,
            }, cancellationToken);
        }
        catch
        {
            _calls.TryRemove(call.CallId, out _);
            throw;
        }
        call.Start();
        _completions[call.CallId] = CompleteAsync(call);
        return true;
    }

    /// <summary>
    /// Ends a call owned by the tenant and agent, on whichever replica controls it, or returns the summary of one
    /// that already ended; returns <see langword="null"/> for an unknown, foreign or expired call.
    /// </summary>
    public async Task<RealtimeCallSummaryResponse?> EndAsync(
        string tenantId, string agentName, string callId, CancellationToken cancellationToken)
    {
        if (_calls.TryGetValue(callId, out var call) && IsOwner(call, tenantId, agentName))
            return await call.EndAsync(RealtimeCallEndReason.ClientRequested);

        var record = await store.RequestEndAsync(tenantId, agentName, callId, cancellationToken);
        var waitUntil = timeProvider.GetUtcNow() + RemoteEndWait;
        while (record is { Status: not RealtimeCallStatus.Ended } && timeProvider.GetUtcNow() < waitUntil)
        {
            await Task.Delay(RemoteEndPoll, timeProvider, cancellationToken);
            record = await store.GetAsync(tenantId, agentName, callId, cancellationToken);
        }
        return record switch
        {
            null => null,
            { Summary: { } summary } => summary,
            // The end is recorded and the owner or a sweeper hangs up; report what is known so far.
            _ => CreateSummary(record, RealtimeCallEndReason.ClientRequested, timeProvider.GetUtcNow()),
        };
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var call in _calls.Values)
            call.RequestEnd(RealtimeCallEndReason.HostShutdown);
        await Task.WhenAll(_completions.Values).WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RenewInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await MaintainAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                        logger.LogWarning(ex, "{ClassName} could not maintain realtime call leases", nameof(RealtimeCallRegistry));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping; StopAsync ends this instance's calls.
        }
    }

    internal async Task MaintainAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var call in _calls.Values)
        {
            if (await store.RenewAsync(call.CallId, OwnerId, now + LeaseDuration, call.CurrentUsage, cancellationToken))
                continue;
            // Either another replica asked to end the call or this instance lost it to a sweeper.
            var record = await store.GetAsync(call.TenantId, call.AgentName, call.CallId, cancellationToken);
            call.RequestEnd(record?.Status == RealtimeCallStatus.Ending
                ? RealtimeCallEndReason.ClientRequested
                : RealtimeCallEndReason.ControlLost);
        }

        foreach (var record in await store.ClaimExpiredAsync(OwnerId, now, now + LeaseDuration, cancellationToken))
        {
            // A local call outlived its lease during a store outage; the claim renewed it, so keep controlling it.
            if (!_calls.ContainsKey(record.CallId))
                await SweepAsync(record, now, cancellationToken);
        }

        await store.DeleteEndedBeforeAsync(now - EndedRetention, cancellationToken);
    }

    private async Task CompleteAsync(RealtimeCallSession call)
    {
        await call.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _calls.TryRemove(call.CallId, out _);
        try
        {
            await store.CompleteAsync(call.CallId, call.Summary!, timeProvider.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            // The lease lapses and a sweeper completes the record with the last reported usage.
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(ex, "{ClassName} could not record a realtime call's end", nameof(RealtimeCallRegistry));
        }
        finally
        {
            _completions.TryRemove(call.CallId, out _);
        }
    }

    private async Task SweepAsync(RealtimeCallRecord record, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var credentials = ActivatorUtilities.CreateInstance<ConfigurationProviderCredentialStore>(
            scope.ServiceProvider, new FixedTenantContext(record.TenantId));
        var credential = await RealtimeProviderCredential.ResolveAsync(credentials, record.ProviderName, cancellationToken);
        var accepted = false;
        if (credential is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HangUpTimeout);
            try
            {
                accepted = await scope.ServiceProvider.GetRequiredService<AzureRealtimeClient>()
                    .HangUpAsync(record.Endpoint, credential, record.ProviderCallId, timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (logger.IsEnabled(LogLevel.Warning))
                    logger.LogWarning(ex, "{ClassName} could not ask the provider to end a swept realtime call",
                        nameof(RealtimeCallRegistry));
            }
        }
        var reason = record.Status == RealtimeCallStatus.Ending
            ? RealtimeCallEndReason.ClientRequested
            : RealtimeCallEndReason.ControlLost;
        await store.CompleteAsync(record.CallId, CreateSummary(record, reason, now), now, cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "{ClassName} swept a realtime call whose owner's lease expired; end reason {EndReason}, provider hang-up accepted {Accepted}",
                nameof(RealtimeCallRegistry), reason, accepted);
    }

    private static RealtimeCallSummaryResponse CreateSummary(
        RealtimeCallRecord record, RealtimeCallEndReason reason, DateTimeOffset now) => new()
        {
            EndReason = reason,
            DurationSeconds = (int)(now - record.StartedAt).TotalSeconds,
            Usage = record.Usage ?? new RealtimeCallUsage(),
        };

    private static bool IsOwner(RealtimeCallSession call, string tenantId, string agentName) =>
        call.TenantId.Equals(tenantId, StringComparison.Ordinal) && call.AgentName.Equals(agentName, StringComparison.Ordinal);
}
