namespace CasCap.IntegrationTests.Local;

/// <summary>Runs a test only when live Agent Runtime testing is explicitly enabled.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalAgentRuntimeFactAttribute : FactAttribute
{
    /// <summary>Initializes a local-only Agent Runtime test.</summary>
    public LocalAgentRuntimeFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Live Agent Runtime tests require CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS=true outside CI.";
        SkipUnless = nameof(LocalAgentRuntimeTestEnvironment.IsEnabled);
        SkipType = typeof(LocalAgentRuntimeTestEnvironment);
    }
}