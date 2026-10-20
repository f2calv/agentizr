namespace CasCap.AgentRuntime.Execution.Services;

/// <summary>Removes reasoning markup that a model leaked into its answer text.</summary>
/// <remarks>
/// A reasoning model normally returns its thinking separately, but a server that fails to parse it
/// leaves <c>&lt;think&gt;</c> markup in the answer. When the chat template opens the block in the
/// prompt, only a closing tag appears, and everything before it is reasoning.
/// </remarks>
public static class ReasoningMarkupFilter
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    /// <summary>Returns <paramref name="text"/> without reasoning markup.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="removedCharacters">How many characters were removed; zero when the text was clean.</param>
    public static string Strip(string text, out int removedCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains(OpenTag, StringComparison.OrdinalIgnoreCase)
            && !text.Contains(CloseTag, StringComparison.OrdinalIgnoreCase))
        {
            removedCharacters = 0;
            return text;
        }

        var result = text;

        // Complete blocks first, so a later closing tag is only ever an orphan.
        int open;
        while ((open = result.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var close = result.IndexOf(CloseTag, open + OpenTag.Length, StringComparison.OrdinalIgnoreCase);
            // An unclosed block runs to the end of the answer.
            result = close < 0
                ? result[..open]
                : result[..open] + result[(close + CloseTag.Length)..];
        }

        var orphanClose = result.LastIndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
        if (orphanClose >= 0)
            result = result[(orphanClose + CloseTag.Length)..];

        result = result.Trim();
        removedCharacters = text.Length - result.Length;
        return result;
    }
}
