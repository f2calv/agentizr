namespace CasCap.Abstractions;

/// <summary>Serializes changes to one tenant-qualified agent session across definition versions.</summary>
public interface IAgentSessionLock
{
    /// <summary>Acquires exclusive ownership or throws a session conflict without waiting.</summary>
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken);
}
