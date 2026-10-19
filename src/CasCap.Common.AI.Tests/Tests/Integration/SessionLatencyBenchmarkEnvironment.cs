namespace CasCap.Common.AI.Tests.Integration;

/// <summary>Controls whether and how the session-size latency benchmarks execute.</summary>
public static class SessionLatencyBenchmarkEnvironment
{
    private static readonly int[] DefaultTurnCounts = [0, 5, 10, 20];

    /// <summary>Gets whether the benchmarks were explicitly enabled outside CI.</summary>
    public static bool IsEnabled =>
        !CIEnvironment.IsCI
        && bool.TryParse(Environment.GetEnvironmentVariable("CASCAP_RUN_SESSION_LATENCY_BENCHMARKS"), out var enabled)
        && enabled;

    /// <summary>Gets the number of measured samples per cell; defaults to 3.</summary>
    public static int Samples =>
        int.TryParse(Environment.GetEnvironmentVariable("CASCAP_SESSION_LATENCY_SAMPLES"), out var samples) && samples > 0
            ? samples
            : 3;

    /// <summary>Gets the conversation turn counts to measure; defaults to 0, 5, 10 and 20.</summary>
    /// <remarks>Each turn adds a user message, a tool call, a tool result and an assistant answer.</remarks>
    public static IReadOnlyList<int> TurnCounts
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("CASCAP_SESSION_LATENCY_TURNS");
            if (string.IsNullOrWhiteSpace(value))
                return DefaultTurnCounts;

            var turnCounts = new List<int>();
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var turns) || turns < 0)
                    return DefaultTurnCounts;
                turnCounts.Add(turns);
            }
            return turnCounts.Count > 0 ? turnCounts : DefaultTurnCounts;
        }
    }

    /// <summary>Gets the reducer target applied to the compacted history; defaults to 20.</summary>
    /// <remarks>Matches the <c>maxMessages</c> of the published group-chat agent definitions.</remarks>
    public static int ReducerTarget =>
        int.TryParse(Environment.GetEnvironmentVariable("CASCAP_SESSION_LATENCY_REDUCER_TARGET"), out var target) && target > 0
            ? target
            : 20;
}
