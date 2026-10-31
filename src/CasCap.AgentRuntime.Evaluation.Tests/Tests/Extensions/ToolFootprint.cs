namespace CasCap.AgentRuntime.Evaluation.Tests.Extensions;

/// <summary>Estimates the token footprint of synthetic tool schemas in evaluation reports.</summary>
internal static class ToolFootprint
{
    /// <summary>A rough English token estimate for a character count.</summary>
    /// <param name="characters">The character count.</param>
    public static int ApproximateTokens(int characters) => (characters + 3) / 4;
}
