namespace CasCap.Abstractions;

/// <summary>Executes one fully resolved agent turn.</summary>
public interface IAgentExecutor
{
    /// <summary>Executes the supplied context and returns output plus updated session state.</summary>
    public ValueTask<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken);
}
