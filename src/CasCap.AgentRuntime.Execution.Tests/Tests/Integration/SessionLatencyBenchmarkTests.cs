using System.Diagnostics;

namespace CasCap.AgentRuntime.Execution.Tests.Integration;

/// <summary>
/// Measures how conversation history size affects provider latency, comparing uncompacted history
/// with the output of the runtime's <see cref="ToolOutputStrippingChatReducer"/>.
/// </summary>
/// <remarks>
/// Every sample starts with a unique system-message nonce so neither llama.cpp prompt caching nor
/// Azure OpenAI prompt caching can reuse an earlier prefix; the figures are uncached prefill costs.
/// Output is capped so the measured time is dominated by prompt processing rather than generation.
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Category", "Session Latency")]
public sealed class SessionLatencyBenchmarkTests(ITestOutputHelper output)
{
    private enum HistoryShape
    {
        Uncompacted,
        Reduced,
    }

    private sealed record Sample(TimeSpan FirstToken, TimeSpan Total, long? InputTokens);

    private sealed record Cell(int Turns, HistoryShape Shape, int MessageCount, IReadOnlyList<Sample> Samples);

    private const int GroupCount = 12;
    private const string Instructions =
        "You are a concise operations assistant for a messaging gateway. Answer from the conversation so far.";
    private const string FinalQuestion = "Summarise the latest gateway status in one short sentence.";

    [SessionLatencyBenchmarkFact]
    public async Task GetStreamingResponseAsync_LlamaCppLatencyBySessionSize()
    {
        var configured = LocalLlamaCppTestSettings.TryLoad(out var settings);
        Assert.SkipUnless(
            configured,
            "Set CASCAP_LOCAL_LLAMA_CPP_ENDPOINT and CASCAP_LOCAL_LLAMA_CPP_MODEL to benchmark llama.cpp.");
        var provider = new ProviderConfig
        {
            Type = AgentType.OpenAI,
            Endpoint = settings!.Endpoint,
            ModelName = settings.ModelName,
            ApiKey = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_API_KEY") ?? "sk-no-key-required",
        };
        var (chatClient, _, _) = AgentExtensions.CreateAgent(
            provider,
            CreateAgentConfig("local-llama-cpp"),
            settings.CreateHttpClient());
        using (chatClient)
        {
            await RunBenchmarkAsync("llama.cpp", chatClient, new ChatOptions { MaxOutputTokens = 64 }, settings.Timeout);
        }
    }

    [SessionLatencyBenchmarkFact]
    public async Task GetStreamingResponseAsync_AzureOpenAiLatencyBySessionSize()
    {
        var configured = LocalAzureOpenAiTestSettings.TryLoad(out var settings);
        Assert.SkipUnless(
            configured,
            "Set CASCAP_LOCAL_AZURE_OPENAI_ENDPOINT and CASCAP_LOCAL_AZURE_OPENAI_DEPLOYMENT to benchmark Azure OpenAI.");
        var provider = new ProviderConfig
        {
            Type = AgentType.AzureOpenAI,
            Endpoint = settings!.Endpoint,
            ModelName = settings.DeploymentName,
        };
        var (chatClient, _, _) = AgentExtensions.CreateAgent(
            provider,
            CreateAgentConfig("local-azure-openai"),
            tokenCredential: settings.CreateCredential());
        using (chatClient)
        {
            // Hidden reasoning counts against the output budget, so low effort and a larger cap keep
            // visible text reachable without letting reasoning time dominate the measurement.
            var options = new ChatOptions
            {
                MaxOutputTokens = 1024,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
            };
            await RunBenchmarkAsync("Azure OpenAI", chatClient, options, settings.Timeout);
        }
    }

    private async Task RunBenchmarkAsync(string providerName, IChatClient chatClient, ChatOptions options, TimeSpan timeout)
    {
        var reducerTarget = SessionLatencyBenchmarkEnvironment.ReducerTarget;
        var reducer = new ToolOutputStrippingChatReducer(reducerTarget);
        var sampleCount = SessionLatencyBenchmarkEnvironment.Samples;

        // The warm-up keeps model loading and connection setup out of the first measured cell.
        await MeasureAsync(chatClient, BuildMessages(BuildHistory(0)), options, timeout);

        var cells = new List<Cell>();
        foreach (var turns in SessionLatencyBenchmarkEnvironment.TurnCounts)
        {
            foreach (var shape in Enum.GetValues<HistoryShape>())
            {
                var samples = new List<Sample>(sampleCount);
                var messageCount = 0;
                for (var sample = 0; sample < sampleCount; sample++)
                {
                    var history = BuildHistory(turns);
                    if (shape == HistoryShape.Reduced)
                        history = [.. await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)];

                    var messages = BuildMessages(history);
                    messageCount = messages.Count;
                    samples.Add(await MeasureAsync(chatClient, messages, options, timeout));
                }
                cells.Add(new Cell(turns, shape, messageCount, samples));
            }
        }

        WriteTable(providerName, reducerTarget, sampleCount, options, cells);

        foreach (var turns in SessionLatencyBenchmarkEnvironment.TurnCounts.Where(turns => turns > 0))
        {
            var uncompacted = MedianInputTokens(cells.Single(c => c.Turns == turns && c.Shape == HistoryShape.Uncompacted));
            var reduced = MedianInputTokens(cells.Single(c => c.Turns == turns && c.Shape == HistoryShape.Reduced));
            if (uncompacted is not null && reduced is not null)
                Assert.True(reduced < uncompacted, $"Reduced history at {turns} turns used {reduced} input tokens, uncompacted used {uncompacted}.");
        }
    }

    private static async Task<Sample> MeasureAsync(
        IChatClient chatClient,
        List<ChatMessage> messages,
        ChatOptions options,
        TimeSpan timeout)
    {
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        UsageDetails? usage = null;
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationTokenSource.Token))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 }:
                    case TextReasoningContent { Text.Length: > 0 }:
                        firstToken ??= stopwatch.Elapsed;
                        break;
                    case UsageContent usageContent:
                        usage = usageContent.Details;
                        break;
                }
            }
        }
        stopwatch.Stop();

        Assert.True(firstToken.HasValue, "The provider streamed no text or reasoning content.");
        return new Sample(firstToken.Value, stopwatch.Elapsed, usage?.InputTokenCount);
    }

    /// <summary>Builds a synthetic history where every turn calls a status tool with a verbose JSON result.</summary>
    private static List<ChatMessage> BuildHistory(int turns)
    {
        var messages = new List<ChatMessage>(1 + (turns * 4))
        {
            new(ChatRole.System, $"Session {Guid.NewGuid():N}. {Instructions}"),
        };
        for (var turn = 1; turn <= turns; turn++)
        {
            var callId = $"call_{turn}";
            messages.Add(new ChatMessage(ChatRole.User, $"Check {turn}: which groups are connected and is the gateway healthy?"));
            messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, "get_gateway_status")]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, BuildToolResult(turn))]));
            messages.Add(new ChatMessage(
                ChatRole.Assistant,
                $"Check {turn}: the gateway is healthy, {GroupCount} groups are connected and {turn % 4} messages are pending."));
        }
        return messages;
    }

    private static List<ChatMessage> BuildMessages(List<ChatMessage> history) =>
        [.. history, new ChatMessage(ChatRole.User, FinalQuestion)];

    private static string BuildToolResult(int turn)
    {
        var builder = new StringBuilder("{\"healthy\":true,\"check\":").Append(turn).Append(",\"groups\":[");
        for (var group = 1; group <= GroupCount; group++)
        {
            if (group > 1)
                builder.Append(',');
            builder.Append("{\"name\":\"Example group ").Append(group)
                .Append("\",\"subscribers\":").Append((group % 3) + 1)
                .Append(",\"lastMessageUtc\":\"2026-01-01T00:00:00Z\",\"pending\":").Append((group + turn) % 4)
                .Append('}');
        }
        return builder.Append("]}").ToString();
    }

    private void WriteTable(string providerName, int reducerTarget, int sampleCount, ChatOptions options, List<Cell> cells)
    {
        output.WriteLine(
            $"{providerName}: {sampleCount} uncached samples per cell, reducer target {reducerTarget}, " +
            $"max {options.MaxOutputTokens} output tokens, reasoning effort {options.Reasoning?.Effort?.ToString() ?? "default"}.");
        output.WriteLine("| Turns | History | Messages | Input tokens | First token (median) | Total (median) | First token (range) |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var cell in cells)
        {
            var firstTokens = cell.Samples.Select(s => s.FirstToken).Order().ToList();
            output.WriteLine(
                $"| {cell.Turns} | {cell.Shape} | {cell.MessageCount} | {MedianInputTokens(cell)?.ToString() ?? "n/a"} " +
                $"| {Median(firstTokens).TotalMilliseconds:N0} ms " +
                $"| {Median([.. cell.Samples.Select(s => s.Total).Order()]).TotalMilliseconds:N0} ms " +
                $"| {firstTokens[0].TotalMilliseconds:N0}-{firstTokens[^1].TotalMilliseconds:N0} ms |");
        }
    }

    private static TimeSpan Median(List<TimeSpan> sorted) => sorted[(sorted.Count - 1) / 2];

    private static long? MedianInputTokens(Cell cell)
    {
        var tokens = cell.Samples.Where(s => s.InputTokens.HasValue).Select(s => s.InputTokens!.Value).Order().ToList();
        return tokens.Count == 0 ? null : tokens[(tokens.Count - 1) / 2];
    }

    private static AgentConfig CreateAgentConfig(string providerKey) => new()
    {
        Provider = providerKey,
        Name = "session-latency-benchmark",
        Description = "Session-size latency benchmark agent.",
        Prompt = FinalQuestion,
        Instructions = Instructions,
    };
}
