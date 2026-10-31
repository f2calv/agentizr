namespace CasCap.AgentRuntime.Evaluation.Tests.Unit;

/// <summary>Tests for deterministic answer expectations and tool-call grading.</summary>
[Trait("Category", "Agent Evaluation")]
public sealed class AgentEvaluationGradingTests
{
    [Theory]
    [InlineData("There are 5 lights on.", 5d, 0d, true)]
    [InlineData("Five of the twelve lights are switched on.", 5d, 0d, true)]
    [InlineData("7 lights are on.", 5d, 0d, false)]
    [InlineData("It is 7,5 °C on the north side.", 7.5d, 0.05d, true)]
    [InlineData("It is 7.54°C outside.", 7.5d, 0.05d, true)]
    [InlineData("It is 21.3 °C in the kitchen.", 7.5d, 0.05d, false)]
    public void AnswerExpectation_Number(string answer, double expected, double tolerance, bool satisfied) =>
        Assert.Equal(satisfied, AnswerExpectation.Number(expected, tolerance).IsSatisfiedBy(answer));

    [Theory]
    [InlineData("The front door is unlocked.", true)]
    [InlineData("The front door is locked.", false)]
    public void AnswerExpectation_ContainsNone_MatchesWholeWords(string answer, bool satisfied) =>
        Assert.Equal(satisfied, AnswerExpectation.ContainsNone("locked").IsSatisfiedBy(answer));

    [Theory]
    [InlineData("Five lights are on in the kitchen.", true)]
    [InlineData("Five lights are on in the office.", false)]
    [InlineData("Seven lights are on in the kitchen.", false)]
    public void AnswerExpectation_All_RequiresEveryPart(string answer, bool satisfied) =>
        Assert.Equal(satisfied, AnswerExpectation.All(AnswerExpectation.Number(5), AnswerExpectation.ContainsAny("kitchen"))
            .IsSatisfiedBy(answer));

    [Theory]
    [InlineData("Order 42 has shipped.", true)]
    [InlineData("Order 41 has shipped; order 42 is pending.", false)]
    public void AnswerExpectation_Matches_AnchorsToEntity(string answer, bool satisfied) =>
        Assert.Equal(satisfied, AnswerExpectation.Matches(@"order 42 (?:has|is) shipped", "says order 42 shipped")
            .IsSatisfiedBy(answer));

    [Fact]
    public void Grade_FailsMissingToolAndSideEffect()
    {
        RecordedToolCall[] toolCalls =
        [
            new("OrderAgent", "get_customer", ToolDisposition.Query, "{}", FixtureFound: false),
            new("OrderAgent", "cancel_order", ToolDisposition.SideEffect, "{}", FixtureFound: true),
        ];

        var (answerPassed, toolSelectionPassed, reasons) =
            AgentEvaluationGrader.Grade(AgentEvaluationTestData.OrderStatusScenario, "It has shipped.", toolCalls);

        Assert.True(answerPassed);
        Assert.False(toolSelectionPassed);
        Assert.Contains("did not call get_order_status or list_orders", reasons);
        Assert.Contains("attempted side effect cancel_order", reasons);
    }

    [Fact]
    public void Grade_PassesCorrectRun()
    {
        RecordedToolCall[] toolCalls = [new("OrderAgent", "get_order_status", ToolDisposition.Query, "{}", FixtureFound: true)];

        var (answerPassed, toolSelectionPassed, reasons) =
            AgentEvaluationGrader.Grade(AgentEvaluationTestData.OrderStatusScenario, "It has shipped.", toolCalls);

        Assert.True(answerPassed);
        Assert.True(toolSelectionPassed);
        Assert.Empty(reasons);
    }
}
