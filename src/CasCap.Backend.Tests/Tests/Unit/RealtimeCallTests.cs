using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Tests realtime session configuration, tool dispatch and provider brokering without a live service.</summary>
[Trait("Category", "Realtime")]
public sealed class RealtimeCallTests
{
    [Fact]
    public void Build_ClientSecretSessionCarriesDefinitionInstructionsModelAndTools()
    {
        var tool = AIFunctionFactory.Create(([Description("Word")] string term) => term, "add_vocabulary", "Adds a word.");

        var session = RealtimeSessionConfiguration.Build(CreateDefinition(), [tool], includeModel: true);

        Assert.Equal("realtime", session["type"]!.GetValue<string>());
        Assert.Equal("gpt-realtime", session["model"]!.GetValue<string>());
        Assert.Equal("Tutor in German.", session["instructions"]!.GetValue<string>());
        Assert.Equal("semantic_vad", session["audio"]!["input"]!["turn_detection"]!["type"]!.GetValue<string>());
        Assert.Equal("low", session["audio"]!["input"]!["turn_detection"]!["eagerness"]!.GetValue<string>());
        Assert.Equal("de", session["audio"]!["input"]!["transcription"]!["language"]!.GetValue<string>());
        Assert.Equal("marin", session["audio"]!["output"]!["voice"]!.GetValue<string>());
        var declared = Assert.Single(session["tools"]!.AsArray());
        Assert.Equal("add_vocabulary", declared!["name"]!.GetValue<string>());
        Assert.Equal("auto", session["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public void Build_SessionUpdateOmitsModelAndDisablesToolsWhenNoneAreAllowed()
    {
        var session = RealtimeSessionConfiguration.Build(CreateDefinition(), [], includeModel: false);

        Assert.False(session.ContainsKey("model"));
        Assert.Empty(session["tools"]!.AsArray());
        Assert.Equal("none", session["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeAsync_ExecutesAllowlistedToolOncePerCallId()
    {
        var invocations = 0;
        var tool = AIFunctionFactory.Create((string term) => { invocations++; return $"saved {term}"; }, "add_vocabulary");
        var dispatcher = new RealtimeToolDispatcher([tool], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var first = await dispatcher.InvokeAsync("call-1", "add_vocabulary", """{"term":"Haus"}""", TestContext.Current.CancellationToken);
        var repeated = await dispatcher.InvokeAsync("call-1", "add_vocabulary", """{"term":"Haus"}""", TestContext.Current.CancellationToken);

        Assert.Equal("\"saved Haus\"", first);
        Assert.Null(repeated);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task InvokeAsync_RefusesToolsOutsideTheAllowlist()
    {
        var dispatcher = new RealtimeToolDispatcher([], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var output = await dispatcher.InvokeAsync("call-1", "record_mistake", "{}", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(output!);
        Assert.Contains("not available", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonObjectArguments()
    {
        var tool = AIFunctionFactory.Create(() => "ok", "get_learner_profile");
        var dispatcher = new RealtimeToolDispatcher([tool], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var output = await dispatcher.InvokeAsync("call-1", "get_learner_profile", "[1]", TestContext.Current.CancellationToken);

        Assert.Contains("error", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCallAsync_PostsOfferWithClientSecretAndReturnsProviderCallId()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync();
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("v=0 answer") };
            response.Headers.Location = new Uri("/v1/realtime/calls/rtc_example", UriKind.Relative);
            return response;
        });
        var client = new AzureRealtimeClient(new HttpClient(handler));

        var (answer, callId) = await client.CreateCallAsync(
            new Uri("https://example.openai.azure.com/"), "ek_example", "v=0 offer", TestContext.Current.CancellationToken);

        Assert.Equal("v=0 answer", answer);
        Assert.Equal("rtc_example", callId);
        Assert.Equal("https://example.openai.azure.com/openai/v1/realtime/calls?webrtcfilter=on", sent!.RequestUri!.ToString());
        Assert.Equal("Bearer ek_example", sent.Headers.Authorization!.ToString());
        Assert.Equal("application/sdp", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("v=0 offer", body);
    }

    [Fact]
    public async Task CreateClientSecretAsync_FailureReportsStatusWithoutResponseBody()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("private provider detail"),
        }));
        var client = new AzureRealtimeClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateClientSecretAsync(
            new Uri("https://example.openai.azure.com/"),
            RealtimeProviderCredential.FromApiKey("example-key"),
            [],
            TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.DoesNotContain("private provider detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetControlUri_UsesSecureWebSocketWithCallId()
    {
        var uri = AzureRealtimeClient.GetControlUri(new Uri("https://example.openai.azure.com/"), "rtc_example");

        Assert.Equal("wss://example.openai.azure.com/openai/v1/realtime?call_id=rtc_example", uri.ToString());
    }

    [Theory]
    [InlineData("vad", "low")]
    [InlineData("semantic_vad", "eager")]
    public void Validate_RejectsUnknownTurnDetectionSettings(string turnDetection, string eagerness)
    {
        var config = CreateDefinition().Realtime! with { TurnDetection = turnDetection, Eagerness = eagerness };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void UsageMeter_SumsResponsesAndEstimatesCostFromConfiguredPrices()
    {
        // Two response.done usage blocks: cached input is priced at the cached rate, not counted twice.
        var meter = new RealtimeCallUsageMeter();
        meter.Add(JsonDocument.Parse("""
            {"usage":{"total_tokens":1700,"input_token_details":{"text_tokens":400,"audio_tokens":600,
              "cached_tokens_details":{"text_tokens":100,"audio_tokens":200}},
              "output_token_details":{"text_tokens":100,"audio_tokens":600}}}
            """).RootElement);
        meter.Add(JsonDocument.Parse("""{"usage":{"output_token_details":{"audio_tokens":600}}}""").RootElement);
        meter.Add(JsonDocument.Parse("""{"status":"cancelled"}""").RootElement);

        var usage = meter.ToUsage();
        var cost = meter.EstimateCost(new RealtimeTokenPricing
        {
            Currency = "EUR",
            TextInputPerMillion = 4m,
            CachedTextInputPerMillion = 0.4m,
            AudioInputPerMillion = 32m,
            CachedAudioInputPerMillion = 0.4m,
            TextOutputPerMillion = 16m,
            AudioOutputPerMillion = 64m,
        });

        Assert.Equal(2300, usage.TotalTokens);
        Assert.Equal(300, usage.CachedInputTokens);
        Assert.Equal(1200, usage.OutputAudioTokens);
        // 300*4 + 100*0.4 + 400*32 + 200*0.4 + 100*16 + 1200*64 = 92520 per million tokens.
        Assert.Equal(0.0925m, cost!.Amount);
        Assert.Equal("EUR", cost.Currency);
        Assert.Null(meter.EstimateCost(null));
    }

    [Fact]
    public async Task LoadToolsAsync_AllowlistedBuiltInOutlivesTheSetupRequest()
    {
        // The datetime built-in is not an MCP tool; a call must still expose it and answer after setup returns.
        var tenant = new RequestTenantContext();
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants = { ["tenant-a"] = new() { TimeZoneId = "UTC" } },
            }))
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .BuildServiceProvider();
        var definition = CreateDefinition() with
        {
            Agent = CreateDefinition().Agent with { IncludeRuntimeContext = true },
            Realtime = CreateDefinition().Realtime! with { Tools = ["get_current_datetime_state"] },
        };
        var service = new RealtimeCallService(
            new SingleDefinitionStore(definition), null!, null!, tenant, null!,
            CreateRegistry(new InMemoryRealtimeCallStore(), services),
            Options.Create(new AgentRuntimeConfig()), services, NullLoggerFactory.Instance);

        var tools = await service.LoadToolsAsync(definition, definition.Realtime!, [], NullLogger.Instance, TestContext.Current.CancellationToken);
        tenant.EndRequest();
        var result = await Assert.Single(tools).InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.Equal("get_current_datetime_state", tools[0].Name);
        Assert.Contains("UTC", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallStore_EndRequestIsTenantScopedAndStopsTheOwnersRenewal()
    {
        var store = new InMemoryRealtimeCallStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(CreateRecord("owner-a", now.AddSeconds(20)), TestContext.Current.CancellationToken);

        var foreign = await store.RequestEndAsync("tenant-b", "tutor", "call-1", TestContext.Current.CancellationToken);
        var renewed = await store.RenewAsync("call-1", "owner-a", now.AddSeconds(40), new RealtimeCallUsage(), TestContext.Current.CancellationToken);
        var requested = await store.RequestEndAsync("tenant-a", "tutor", "call-1", TestContext.Current.CancellationToken);
        var renewedAfterEnd = await store.RenewAsync("call-1", "owner-a", now.AddSeconds(60), new RealtimeCallUsage(), TestContext.Current.CancellationToken);

        Assert.Null(foreign);
        Assert.True(renewed);
        Assert.Equal(RealtimeCallStatus.Ending, requested!.Status);
        Assert.False(renewedAfterEnd);
    }

    [Fact]
    public async Task CallStore_ClaimsAnExpiredLeaseOnce()
    {
        var store = new InMemoryRealtimeCallStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(CreateRecord("owner-a", now.AddSeconds(-1)), TestContext.Current.CancellationToken);

        var first = await store.ClaimExpiredAsync("owner-b", now, now.AddSeconds(20), TestContext.Current.CancellationToken);
        var second = await store.ClaimExpiredAsync("owner-c", now, now.AddSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal("owner-b", Assert.Single(first).OwnerId);
        Assert.Empty(second);
    }

    [Fact]
    public async Task Registry_EndOnANonOwningReplicaReturnsTheOwnersSummary()
    {
        var store = new InMemoryRealtimeCallStore();
        await store.AddAsync(CreateRecord("other-replica", DateTimeOffset.UtcNow.AddSeconds(20)), TestContext.Current.CancellationToken);
        var registry = CreateRegistry(store, new ServiceCollection().BuildServiceProvider());
        var summary = new RealtimeCallSummaryResponse
        {
            EndReason = RealtimeCallEndReason.ClientRequested,
            DurationSeconds = 42,
            Usage = new RealtimeCallUsage { TotalTokens = 1000 },
        };

        var ending = registry.EndAsync("tenant-a", "tutor", "call-1", TestContext.Current.CancellationToken);
        // The owning replica sees the end request on its next renewal, hangs up and records the summary.
        while ((await store.GetAsync("tenant-a", "tutor", "call-1", TestContext.Current.CancellationToken))!.Status != RealtimeCallStatus.Ending)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        await store.CompleteAsync("call-1", summary, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(summary, await ending);
    }

    [Fact]
    public async Task Registry_SweepHangsUpAnExpiredOwnersCallAndRecordsItsEnd()
    {
        HttpRequestMessage? hangUp = null;
        var handler = new StubHandler(request =>
        {
            hangUp = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants =
                {
                    ["tenant-a"] = new()
                    {
                        Providers =
                        {
                            ["realtime"] = new ProviderConfig { Type = AgentType.AzureOpenAI, ModelName = "gpt-realtime", ApiKey = "example-key" },
                        },
                    },
                },
            }))
            .AddSingleton(new RuntimeEntraCredential(() => null))
            .AddSingleton(new AzureRealtimeClient(new HttpClient(handler)))
            .BuildServiceProvider();
        var store = new InMemoryRealtimeCallStore();
        await store.AddAsync(CreateRecord("lost-replica", DateTimeOffset.UtcNow.AddSeconds(-1)) with
        {
            Usage = new RealtimeCallUsage { TotalTokens = 500 },
        }, TestContext.Current.CancellationToken);

        await CreateRegistry(store, services).MaintainAsync(TestContext.Current.CancellationToken);

        var record = await store.GetAsync("tenant-a", "tutor", "call-1", TestContext.Current.CancellationToken);
        Assert.Equal(RealtimeCallStatus.Ended, record!.Status);
        Assert.Equal(RealtimeCallEndReason.ControlLost, record.Summary!.EndReason);
        Assert.Equal(500, record.Summary.Usage.TotalTokens);
        Assert.Contains("rtc_example", hangUp!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("example-key", Assert.Single(hangUp.Headers.GetValues("api-key")));
    }

    private static RealtimeCallRegistry CreateRegistry(IRealtimeCallStore store, IServiceProvider services) => new(
        Options.Create(new AgentRuntimeConfig()),
        store,
        services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<RealtimeCallRegistry>.Instance);

    private static RealtimeCallRecord CreateRecord(string ownerId, DateTimeOffset leaseExpiresAt) => new()
    {
        CallId = "call-1",
        TenantId = "tenant-a",
        AgentName = "tutor",
        OwnerId = ownerId,
        Status = RealtimeCallStatus.Active,
        ProviderName = "realtime",
        Endpoint = new Uri("https://example.openai.azure.com/"),
        ProviderCallId = "rtc_example",
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        LeaseExpiresAt = leaseExpiresAt,
    };

    private static AgentDefinition CreateDefinition() => new()
    {
        Name = "tutor",
        Version = "1",
        Agent = new AgentConfig
        {
            Name = "Tutor",
            Description = "Tutor",
            Prompt = "Start",
            Provider = "chat",
            Instructions = "Tutor in German.",
        },
        Provider = new ProviderConfig { Type = AgentType.AzureOpenAI, ModelName = "chat-model" },
        Realtime = new AgentRealtimeConfig
        {
            Provider = "realtime",
            Endpoint = new Uri("https://example.openai.azure.com/"),
            Deployment = "gpt-realtime",
            Eagerness = "low",
            TranscriptionModel = "whisper-1",
            TranscriptionLanguage = "de",
            Tools = ["add_vocabulary"],
        },
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }

    private sealed class RequestTenantContext : ITenantContext
    {
        private bool _ended;

        public string TenantId => _ended ? throw new UnauthorizedAccessException("The request has ended.") : "tenant-a";

        public void EndRequest() => _ended = true;
    }

    private sealed class SingleDefinitionStore(AgentDefinition definition) : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDefinition?>(string.Equals(agentName, definition.Name, StringComparison.OrdinalIgnoreCase) ? definition : null);
    }
}
