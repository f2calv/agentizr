using Serilog.Core;
using Serilog.Events;

namespace CasCap.RuntimeConfiguration.Services;

/// <summary>Coordinates actual switch updates and exposes sanitized status independently of log verbosity.</summary>
public sealed class RuntimeLoggingProbe(
    ILogger<RuntimeLoggingProbe> logger,
    TimeProvider timeProvider,
    LoggingLevelSwitch levelSwitch)
{
    private readonly Lock _lock = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly LogEventLevel _startupMinimumLevel = levelSwitch.MinimumLevel;
    private long _applyCount;
    private DateTimeOffset? _lastAppliedAtUtc;

    /// <summary>Reads actual switch/pipeline state without emitting logs or reading source/user data.</summary>
    public RuntimeLoggingStatus ReadStatus()
    {
        lock (_lock)
            return new()
            {
                InstanceId = _instanceId,
                StartupMinimumLevel = _startupMinimumLevel.ToString(),
                EffectiveMinimumLevel = levelSwitch.MinimumLevel.ToString(),
                ProbeDebugEnabled = logger.IsEnabled(LogLevel.Debug),
                ApplyCount = _applyCount,
                LastAppliedAtUtc = _lastAppliedAtUtc,
            };
    }

    /// <summary>Applies an accepted monitor value to the same switch the pipeline consumes.</summary>
    internal void Apply(RuntimeLoggingControls value)
    {
        lock (_lock)
        {
            levelSwitch.MinimumLevel = value.MinimumLevel;
            _applyCount++;
            _lastAppliedAtUtc = timeProvider.GetUtcNow();
        }
    }
}
