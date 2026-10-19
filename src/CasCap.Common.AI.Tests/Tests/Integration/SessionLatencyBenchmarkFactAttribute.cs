namespace CasCap.Common.AI.Tests.Integration;

/// <summary>Runs a session-size latency benchmark only when explicitly enabled outside CI.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SessionLatencyBenchmarkFactAttribute : FactAttribute
{
    /// <summary>Initializes a local-only latency benchmark.</summary>
    public SessionLatencyBenchmarkFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Session latency benchmarks require CASCAP_RUN_SESSION_LATENCY_BENCHMARKS=true outside CI.";
        SkipUnless = nameof(SessionLatencyBenchmarkEnvironment.IsEnabled);
        SkipType = typeof(SessionLatencyBenchmarkEnvironment);
    }
}
