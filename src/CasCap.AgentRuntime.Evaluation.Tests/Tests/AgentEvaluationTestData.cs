namespace CasCap.AgentRuntime.Evaluation.Tests;

/// <summary>Builders for synthetic evaluation runs, scenarios and tool configurations.</summary>
internal static class AgentEvaluationTestData
{
    /// <summary>A scenario that requires one of two query tools and forbids side effects.</summary>
    public static AgentEvaluationScenario OrderStatusScenario { get; } = new()
    {
        Id = "order-status",
        AgentKey = "OrderAgent",
        Question = "What is the status of order 42?",
        Answer = AnswerExpectation.ContainsAny("shipped"),
        RequiredToolGroups = [["get_order_status", "list_orders"]],
    };

    /// <summary>Creates a completed, passing run with a single model request of the given duration.</summary>
    /// <param name="providerKey">The provider key; the model name is derived from it.</param>
    /// <param name="scenarioId">The scenario identifier.</param>
    /// <param name="seconds">The run and request duration.</param>
    public static AgentEvaluationRun CreateRun(string providerKey, string scenarioId, double seconds) => new()
    {
        ScenarioId = scenarioId,
        ProviderKey = providerKey,
        ModelName = $"model-{providerKey}",
        InstructionVariant = "baseline",
        ToolSurfaceVariant = ToolSurfaceVariant.Baseline.Name,
        Repetition = 1,
        Answer = "shipped",
        Elapsed = TimeSpan.FromSeconds(seconds),
        AnswerPassed = true,
        ToolSelectionPassed = true,
        RoundTrips = [new ModelRoundTrip("agent", TimeSpan.FromSeconds(seconds), 1, 10, 2, 100, 50, null, 5, null, "stop", 0)],
    };

    /// <summary>
    /// Creates an agent configuration for deferred tool discovery without configuring a model provider.
    /// </summary>
    /// <param name="toolServiceName">The tool type the order agent uses.</param>
    public static AgentConfig CreateAgentConfig(string toolServiceName) => new()
    {
        Provider = "test",
        Name = "Order Agent",
        Description = "Answers order questions.",
        Prompt = string.Empty,
        Instructions = "You answer order questions.",
        Tools = [new ToolSource { Service = toolServiceName }],
    };
}
