
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
            runtimeLoggingProbe.Apply(value);
        }
        catch (Exception ex)
        {
            LogApplyFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Runtime logging configuration change was rejected; retaining the last valid logging state")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);
}
