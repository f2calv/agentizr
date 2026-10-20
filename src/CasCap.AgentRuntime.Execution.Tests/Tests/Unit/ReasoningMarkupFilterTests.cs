namespace CasCap.AgentRuntime.Execution.Tests.Unit;

/// <summary>Tests that leaked reasoning markup is removed from answer text.</summary>
[Trait("Category", "Reasoning Markup")]
public sealed class ReasoningMarkupFilterTests
{
    [Theory]
    [InlineData("There are 5 lights on.", "There are 5 lights on.")]
    [InlineData("<think>Counting the lights.</think>There are 5.", "There are 5.")]
    [InlineData("<THINK>Counting.</THINK> There are 5.", "There are 5.")]
    [InlineData("Let me check the trades.\n</think>\n\nNo FTSE trades are open.", "No FTSE trades are open.")]
    [InlineData("<think>first</think>Answer <think>second</think>continues.", "Answer continues.")]
    [InlineData("The answer is 5.<think>Unfinished reasoning", "The answer is 5.")]
    public void Strip_RemovesReasoningMarkup(string text, string expected)
    {
        var result = ReasoningMarkupFilter.Strip(text, out var removed);

        Assert.Equal(expected, result);
        Assert.Equal(text.Length - expected.Length, removed);
    }

    [Fact]
    public void Strip_CleanText_ReportsNothingRemoved()
    {
        const string text = "  Unchanged, including surrounding whitespace.  ";

        var result = ReasoningMarkupFilter.Strip(text, out var removed);

        Assert.Same(text, result);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Strip_ReasoningOnly_ReturnsEmpty()
    {
        var result = ReasoningMarkupFilter.Strip("Thinking aloud</think>", out var removed);

        Assert.Equal(string.Empty, result);
        Assert.Equal(22, removed);
    }
}
