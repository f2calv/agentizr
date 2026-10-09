namespace CasCap.IntegrationTests.Local;

/// <summary>Controls whether live Agent Runtime tests may execute.</summary>
public static class LocalAgentRuntimeTestEnvironment
{
    /// <summary>Gets whether tests were explicitly enabled outside CI.</summary>
    public static bool IsEnabled =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
        && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TF_BUILD"))
        && bool.TryParse(Environment.GetEnvironmentVariable("CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS"), out var enabled)
        && enabled;
}