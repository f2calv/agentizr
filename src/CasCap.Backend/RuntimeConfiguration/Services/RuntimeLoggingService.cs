
namespace CasCap.RuntimeConfiguration.Services;

/// <summary>Applies standard monitor changes to this host's actual Serilog pipeline switch.</summary>
public sealed partial class RuntimeLoggingService(
    ILogger<RuntimeLoggingService> logger,
    IOptionsMonitor<RuntimeLoggingControls> config,
    RuntimeLoggingProbe runtimeLoggingProbe
) : IHostedService, IDisposable
{
    private readonly Lock _lock = new();
    private IDisposable? _subscription;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _subscription = config.OnChange((value, _) => ApplyChange(value));
            try
            {
                runtimeLoggingProbe.Apply(config.CurrentValue);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref _subscription, null)?.Dispose();

    private void ApplyChange(RuntimeLoggingControls value)
    {
        try
        {
            // Monitor refreshes can repeat an unchanged value; only an actual level change is a state transition.
            if (runtimeLoggingProbe.Apply(value))
                LogApplied(logger, nameof(RuntimeLoggingService), value.MinimumLevel);
        }
        catch (Exception ex)
        {
            LogApplyFailed(logger, nameof(RuntimeLoggingService), ex);
        }
    }

    [LoggerMessage(LogLevel.Information, "{ClassName} runtime logging minimum changed to {MinimumLevel}")]
    private static partial void LogApplied(ILogger logger, string className, RuntimeLoggingLevel minimumLevel);

    [LoggerMessage(LogLevel.Error, "{ClassName} runtime logging configuration change was rejected; retaining the last valid logging state")]
    private static partial void LogApplyFailed(ILogger logger, string className, Exception exception);
}
