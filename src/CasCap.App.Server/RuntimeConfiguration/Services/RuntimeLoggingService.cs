
namespace CasCap.RuntimeConfiguration.Services;

/// <summary>Applies standard monitor changes to this host's actual Serilog pipeline switch.</summary>
public sealed class RuntimeLoggingService(
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
            _subscription = config.OnChange(runtimeLoggingProbe.Apply);
            runtimeLoggingProbe.Apply(config.CurrentValue);
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

}
