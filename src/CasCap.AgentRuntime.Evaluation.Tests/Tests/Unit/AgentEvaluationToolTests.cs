namespace CasCap.AgentRuntime.Evaluation.Tests.Unit;

/// <summary>
/// Tests for the evaluation tool surface: catalog classification and filtering, fixture tools, variants,
/// and model-facing parameter schemas.
/// </summary>
[Trait("Category", "Agent Evaluation")]
public sealed class AgentEvaluationToolTests
{
    [McpServerToolType]
    public sealed class OrderMcpQueryService
    {
        [McpServerTool, Description("Gets the status of one order.")]
        public string GetOrderStatus([Description("Order number.")] string orderId) => "live";

        [McpServerTool, Description("Lists every order.")]
        public string[] ListOrders() => ["live"];

        [McpServerTool, Description("Cancels one order.")]
        public bool CancelOrder([Description("Order number.")] string orderId) => true;

        [McpServerTool(ReadOnly = true), Description("Checks an order against the warehouse.")]
        public bool AuditOrder([Description("Order number.")] string orderId) => true;
    }

    private static readonly McpToolCatalog Catalog = new([typeof(OrderMcpQueryService)]);

    [Theory]
    [InlineData("get_order_status", ToolDisposition.Query)]
    [InlineData("list_orders", ToolDisposition.Query)]
    [InlineData("cancel_order", ToolDisposition.SideEffect)]
    [InlineData("audit_order", ToolDisposition.Query)]
    public void McpToolCatalog_Classify(string toolName, ToolDisposition expected) =>
        Assert.Equal(expected, Catalog.Classify(GetTool(toolName)));

    [Fact]
    public void McpToolCatalog_GetServiceTools_AppliesFilters()
    {
        var agentConfig = AgentEvaluationTestData.CreateAgentConfig(nameof(OrderMcpQueryService)) with
        {
            Tools = [new ToolSource { Service = nameof(OrderMcpQueryService), ExcludeTools = ["cancel_order"] }],
        };

        var tools = Catalog.GetServiceTools(EmptyServiceProvider.Instance, agentConfig);

        Assert.Equal(["audit_order", "get_order_status", "list_orders"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FixtureToolFunction_ReturnsFixtureAndRecords()
    {
        var recorder = new EvaluationRecorder();
        var tool = CreateFixtureTool("get_order_status", recorder, ToolSurfaceVariant.Baseline,
            args => new { Status = "shipped", OrderId = args["orderId"] });

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["orderId"] = "42" }, TestContext.Current.CancellationToken);

        Assert.Equal("shipped", Assert.IsType<JsonElement>(result).GetProperty("status").GetString());
        var call = Assert.Single(recorder.ToolCalls);
        Assert.Equal(ToolDisposition.Query, call.Disposition);
        Assert.True(call.FixtureFound);
        Assert.Contains("42", call.ArgumentsJson);
    }

    [Fact]
    public async Task FixtureToolFunction_SandboxesSideEffects()
    {
        var recorder = new EvaluationRecorder();
        var tool = CreateFixtureTool("cancel_order", recorder, ToolSurfaceVariant.Baseline, _ => true);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["orderId"] = "42" }, TestContext.Current.CancellationToken);

        Assert.False(Assert.IsType<JsonElement>(result).GetProperty("accepted").GetBoolean());
        Assert.Equal(ToolDisposition.SideEffect, Assert.Single(recorder.ToolCalls).Disposition);
    }

    [Fact]
    public async Task FixtureToolFunction_ReportsMissingFixture()
    {
        var recorder = new EvaluationRecorder();
        var tool = CreateFixtureTool("list_orders", recorder, ToolSurfaceVariant.Baseline, responder: null);

        var result = await tool.InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.True(Assert.IsType<JsonElement>(result).TryGetProperty("error", out _));
        Assert.False(Assert.Single(recorder.ToolCalls).FixtureFound);
    }

    [Theory]
    [InlineData(ToolDisposition.Query)]
    [InlineData(ToolDisposition.SideEffect)]
    public async Task FixtureToolFunction_NeverInvokesExternalTools(ToolDisposition disposition)
    {
        var calls = 0;
        var inner = AIFunctionFactory.Create(() => ++calls, "external_tool");
        var recorder = new EvaluationRecorder();
        var tool = new FixtureToolFunction(inner, "agent", disposition, _ => 42, recorder);

        var result = await tool.InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, calls);
        var call = Assert.Single(recorder.ToolCalls);
        Assert.Equal(disposition, call.Disposition);
        var response = Assert.IsType<JsonElement>(result);
        if (disposition is ToolDisposition.Query)
            Assert.Equal(42, response.GetInt32());
        else
            Assert.False(response.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task FixtureToolFunction_PassesThroughRuntimeReadOnlyTools()
    {
        var calls = 0;
        var inner = AIFunctionFactory.Create(() => ++calls, "runtime_state");
        var recorder = new EvaluationRecorder();
        var tool = new FixtureToolFunction(inner, "agent", ToolDisposition.Delegation, null, recorder);

        var result = await tool.InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Equal(1, Assert.IsType<JsonElement>(result).GetInt32());
        Assert.Equal(ToolDisposition.Delegation, Assert.Single(recorder.ToolCalls).Disposition);
    }

    [Fact]
    public void FixtureToolFunction_AppliesVariantDescriptions()
    {
        var variant = new ToolSurfaceVariant
        {
            Name = "test",
            DescriptionOverrides = new Dictionary<string, string> { ["get_order_status"] = "Rewritten tool." },
            ParameterDescriptionOverrides = new Dictionary<string, string> { ["get_order_status.orderId"] = "Rewritten parameter." },
        };

        var tool = CreateFixtureTool("get_order_status", new EvaluationRecorder(), variant, responder: null);

        Assert.Equal("Rewritten tool.", tool.Description);
        Assert.Equal("Rewritten parameter.",
            tool.JsonSchema.GetProperty("properties").GetProperty("orderId").GetProperty("description").GetString());
    }

    [Fact]
    public void ToolSurfaceVariant_RewriteSchema_RejectsUnknownParameter()
    {
        var variant = new ToolSurfaceVariant
        {
            Name = "test",
            ParameterDescriptionOverrides = new Dictionary<string, string> { ["get_order_status.missing"] = "x" },
        };

        Assert.Throws<InvalidOperationException>(() => variant.RewriteSchema("get_order_status", GetTool("get_order_status").JsonSchema));
    }

    private static AIFunction GetTool(string toolName) => Catalog.GetAllTools().Single(t => t.Name == toolName);

    private static FixtureToolFunction CreateFixtureTool(string toolName, EvaluationRecorder recorder, ToolSurfaceVariant variant,
        Func<AIFunctionArguments, object?>? responder)
    {
        var inner = GetTool(toolName);
        return new FixtureToolFunction(inner, "OrderAgent", Catalog.Classify(inner), responder, recorder,
            variant.GetDescription(toolName), variant.RewriteSchema(toolName, inner.JsonSchema));
    }
}
