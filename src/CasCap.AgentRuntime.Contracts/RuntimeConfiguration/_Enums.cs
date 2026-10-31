namespace CasCap.AgentRuntime.Contracts.RuntimeConfiguration;

/// <summary>Browser-safe logging levels with the existing durable string wire contract.</summary>
public enum RuntimeLoggingLevel
{
    /// <summary>All diagnostic events.</summary>
    Verbose,
    /// <summary>Debugging events and above.</summary>
    Debug,
    /// <summary>Informational events and above.</summary>
    Information,
    /// <summary>Warnings and above.</summary>
    Warning,
    /// <summary>Errors and above.</summary>
    Error,
    /// <summary>Fatal events only.</summary>
    Fatal,
}
