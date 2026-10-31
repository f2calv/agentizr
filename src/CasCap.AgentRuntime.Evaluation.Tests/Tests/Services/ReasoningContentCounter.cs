namespace CasCap.AgentRuntime.Evaluation.Tests.Services;

/// <summary>
/// Counts reasoning characters in synthetic model-response contents.
/// </summary>
internal static partial class ReasoningContentCounter
{
    /// <summary>
    /// Counts characters of reasoning content and of <c>&lt;think&gt;</c> blocks left inside answer text.
    /// </summary>
    /// <param name="contents">The response contents.</param>
    public static int CountCharacters(IEnumerable<AIContent> contents) =>
        contents.Sum(c => c switch
        {
            TextReasoningContent reasoning => reasoning.Text.Length,
            TextContent text => ThinkBlockRegex().Matches(text.Text).Sum(m => m.Groups[1].Value.Trim().Length),
            _ => 0,
        });

    [GeneratedRegex(@"<think>(.*?)(?:</think>|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ThinkBlockRegex();
}
