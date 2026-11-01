namespace CasCap.AgentRuntime.Evaluation.Tests.Unit;

/// <summary>Tests for evaluation reports: cross-model summaries, session files and visible-thinking detection.</summary>
[Trait("Category", "Agent Evaluation")]
public sealed class AgentEvaluationReportTests
{
    [Theory]
    [InlineData(5, 5, 0.566, 1.0)]
    [InlineData(0, 5, 0.0, 0.434)]
    [InlineData(3, 5, 0.231, 0.882)]
    public void WilsonInterval(int successes, int trials, double lower, double upper)
    {
        var (Lower, Upper) = AgentEvaluationReport.WilsonInterval(successes, trials);

        Assert.Equal(lower, Lower, 0.001);
        Assert.Equal(upper, Upper, 0.001);
    }

    [Fact]
    public void Summarise_GroupsRunsAndCountsPasses()
    {
        AgentEvaluationRun[] runs =
        [
            AgentEvaluationTestData.CreateRun("edge", "a", 10),
            AgentEvaluationTestData.CreateRun("edge", "a", 20) with { AnswerPassed = false, FailureReasons = ["wrong"] },
            AgentEvaluationTestData.CreateRun("cloud", "a", 2),
        ];

        var summaries = AgentEvaluationReport.Summarise(runs);

        var edge = summaries.Single(s => s.ProviderKey == "edge");
        Assert.Equal(2, edge.Runs);
        Assert.Equal(1, edge.Passed);
        Assert.Equal(TimeSpan.FromSeconds(10), edge.MedianElapsed);
        Assert.Contains("wrong ×1", edge.TopFailureReasons);
        Assert.Equal(1, summaries.Single(s => s.ProviderKey == "cloud").Passed);
    }

    [Fact]
    public void SummariseProviders_ComparesSpeedOnSharedCells()
    {
        AgentEvaluationRun[] runs =
        [
            AgentEvaluationTestData.CreateRun("edge", "a", 10), AgentEvaluationTestData.CreateRun("edge", "b", 20),
            AgentEvaluationTestData.CreateRun("cloud", "a", 2), AgentEvaluationTestData.CreateRun("cloud", "b", 5),
            AgentEvaluationTestData.CreateRun("cloud", "only-cloud", 100),
        ];

        var summaries = AgentEvaluationReport.SummariseProviders(runs, "edge");

        Assert.Equal(1d, summaries.Single(s => s.ProviderKey == "edge").SpeedupVersusReference!.Value, 3);
        Assert.Equal(Math.Sqrt(5d * 4d), summaries.Single(s => s.ProviderKey == "cloud").SpeedupVersusReference!.Value, 3);
    }

    [Fact]
    public async Task WriteSessionSummary_ReadsRunsBack()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-evaluation-{Guid.NewGuid():N}");
        try
        {
            var directory = await AgentEvaluationReport.WriteAsync(root, "a",
                [AgentEvaluationTestData.CreateRun("edge", "a", 10), AgentEvaluationTestData.CreateRun("cloud", "a", 2)],
                TestContext.Current.CancellationToken);
            await AgentEvaluationReport.AppendPlainChatAsync(root, "cloud", TimeSpan.FromSeconds(0.5), TestContext.Current.CancellationToken);

            var table = await AgentEvaluationReport.WriteSessionSummaryAsync(directory, "edge", TestContext.Current.CancellationToken);

            Assert.Contains("| cloud | model-cloud | 1/1 (100", table);
            Assert.Contains("| 0.5s |", table);
            Assert.Contains("5.0×", table);
            Assert.True(File.Exists(Path.Combine(directory, "a.summary.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("<think>Counting the lights first.</think>There are 5.", 26)]
    [InlineData("<think></think>There are 5.", 0)]
    [InlineData("There are 5.", 0)]
    public void CountReasoningCharacters_DetectsThinkBlocks(string text, int expected) =>
        Assert.Equal(expected, ReasoningContentCounter.CountCharacters([new TextContent(text)]));

    [Fact]
    public void CountReasoningCharacters_CountsReasoningContent() =>
        Assert.Equal(8, ReasoningContentCounter.CountCharacters([new TextReasoningContent("thinking"), new TextContent("5")]));
}
