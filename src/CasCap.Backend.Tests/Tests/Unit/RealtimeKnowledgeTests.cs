using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Constants;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Tests.Unit;

/// <summary>Verifies the opt-in A1 call boundary and non-replayable retrieval without inference or storage.</summary>
[Trait("Category", "Realtime")]
public sealed class RealtimeKnowledgeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tool_ExposesOnlyTopicAndUsesFixedA1WithOneAttemptAcrossDistinctCallIds()
    {
        var submissions = 0;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (request, _) =>
        {
            submissions++;
            Assert.Equal("A1", request.MaximumCefrLevel);
            Assert.Equal(3, request.MaximumResults);
            Assert.Equal("synthetic lesson", request.Query);
            return Task.FromResult(new DocumentKnowledgeOutcome { Response = Response() });
        }, (_, _) => Task.FromResult(true));
        var properties = session.CreateTool().JsonSchema.GetProperty("properties").EnumerateObject().ToArray();
        Assert.Equal("topic", Assert.Single(properties).Name);
        var dispatcher = Dispatcher(session);

        var first = await InvokeAsync(dispatcher, "first");
        var second = await InvokeAsync(dispatcher, "second");

        Assert.Equal("found", first["status"]!.GetValue<string>());
        Assert.Equal("already_used", second["status"]!.GetValue<string>());
        Assert.False(second.ContainsKey("evidence"));
        Assert.Equal(1, submissions);
        Assert.Equal(2, first["evidence"]!["matches"]![0]!["passage"]!["sourceSpans"]![0]!["pageNumber"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("empty", "no_matches")]
    [InlineData("provider", "unavailable")]
    [InlineData("timeout", "unavailable")]
    [InlineData("validation", "unavailable")]
    public async Task Tool_DistinguishesEmptyEvidenceFromFailureWithoutRetry(string variation, string status)
    {
        var calls = 0;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
        {
            calls++;
            return Task.FromResult(variation switch
            {
                "provider" => new DocumentKnowledgeOutcome { ProviderFailed = true },
                "timeout" => new DocumentKnowledgeOutcome { TimedOut = true },
                "validation" => new DocumentKnowledgeOutcome { ValidationError = "Token ceiling exceeded." },
                _ => new DocumentKnowledgeOutcome { Response = new() { Matches = [], EmbeddingInputTokens = 0 } },
            });
        }, (_, _) => Task.FromResult(true));
        var dispatcher = Dispatcher(session);

        Assert.Equal(status, (await InvokeAsync(dispatcher, "first"))["status"]!.GetValue<string>());
        Assert.Equal("already_used", (await InvokeAsync(dispatcher, "second"))["status"]!.GetValue<string>());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Tool_RevalidatesBeforeAndAfterSearchAndSupportsLaterTurnChecks(bool revokeAfterSearch, int expectedCalls)
    {
        var current = revokeAfterSearch;
        var calls = 0;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
        {
            calls++;
            current = false;
            return Task.FromResult(new DocumentKnowledgeOutcome { Response = Response() });
        }, (_, _) => Task.FromResult(current));

        var result = await InvokeAsync(Dispatcher(session), "first");

        Assert.Equal("unavailable", result["status"]!.GetValue<string>());
        Assert.False(result.ContainsKey("evidence"));
        Assert.False(await session.IsCurrentAsync(Token));
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task Tool_RechecksRetainedCitationIdentitiesWithoutEmbeddingAgain()
    {
        var searches = 0;
        var citationChecks = 0;
        var current = true;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
        {
            searches++;
            return Task.FromResult(new DocumentKnowledgeOutcome { Response = Response() });
        }, (passages, _) =>
        {
            if (passages.Count > 0)
            {
                citationChecks++;
                Assert.Equal("index-a", Assert.Single(passages).IndexJobId);
            }
            return Task.FromResult(current);
        });
        await InvokeAsync(Dispatcher(session), "first");
        Assert.True(await session.IsCurrentAsync(Token));
        current = false;
        Assert.False(await session.IsCurrentAsync(Token));
        Assert.Equal(1, searches);
        Assert.Equal(3, citationChecks);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("overlong")]
    public async Task Tool_RejectsInvalidTopicsWithoutConsumingTheAllowance(string topic)
    {
        var searches = 0;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
        {
            searches++;
            return Task.FromResult(new DocumentKnowledgeOutcome { Response = Response() });
        }, (_, _) => Task.FromResult(true));
        var dispatcher = Dispatcher(session);
        var invalid = await dispatcher.InvokeAsync("invalid", RealtimeKnowledgeSession.ToolName,
            JsonSerializer.Serialize(new { topic = topic == "overlong" ? new string('x', 2_001) : topic }), Token);
        Assert.NotNull(JsonNode.Parse(invalid!)!["error"]);
        Assert.Equal(0, searches);
        Assert.Equal("found", (await InvokeAsync(dispatcher, "valid"))["status"]!.GetValue<string>());
        Assert.Equal(1, searches);
    }

    [Fact]
    public async Task Tool_ConcurrentCallsShareOneNonReplayableAllowance()
    {
        var searches = 0;
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), async (_, token) =>
        {
            Interlocked.Increment(ref searches);
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return new() { Response = Response() };
        }, (_, _) => Task.FromResult(true));
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => InvokeAsync(Dispatcher(session), index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Single(results, result => result["status"]!.GetValue<string>() == "found");
        Assert.Equal(7, results.Count(result => result["status"]!.GetValue<string>() == "already_used"));
        Assert.Equal(1, searches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateCall_KnowledgeRequiresAdditionalPermissionEvenWhenDevelopmentIsOpen(bool permitted)
    {
        var definition = Definition();
        await using var services = new ServiceCollection().AddLogging().AddOptions()
            .AddSingleton<RealtimeKnowledgeSessionFactory>().BuildServiceProvider();
        var service = new RealtimeCallService(new Definitions(definition), null!, null!, new FixedTenantContext("tenant-a"),
            null!, null!, Options.Create(new AgentRuntimeConfig()), services, NullLoggerFactory.Instance);
        var claims = new List<Claim> { new("scp", AgentRuntimePermissions.Realtime) };
        if (permitted)
            claims.Add(new("scp", AgentRuntimePermissions.KnowledgeSearch));
        var caller = new ClaimsPrincipal(new ClaimsIdentity(claims, "synthetic"));

        var outcome = await service.CreateAsync(definition.Name, "v=0 offer", Token, caller);

        Assert.NotNull(outcome);
        Assert.Equal(!permitted, outcome.IsKnowledgeForbidden);
        Assert.Equal(permitted, outcome.IsProviderUnavailable);
        Assert.Null(outcome.Response);
    }

    [Fact]
    public void Configuration_AddsEvidenceInstructionsOnlyForAnOptedInCall()
    {
        var definition = Definition();
        var session = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
            Task.FromResult(new DocumentKnowledgeOutcome()), (_, _) => Task.FromResult(true));
        var enabled = RealtimeSessionConfiguration.Build(definition, [session.CreateTool()], true, session);
        var disabled = RealtimeSessionConfiguration.Build(definition, [], true);
        Assert.Contains("untrusted", enabled["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("one topic", enabled["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(definition.Agent.Instructions, disabled["instructions"]!.GetValue<string>());
        Assert.Equal(RealtimeKnowledgeSession.ToolName, Assert.Single(enabled["tools"]!.AsArray())!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("enabled", true)]
    [InlineData("disabled", false)]
    [InlineData("not-opted-in", false)]
    [InlineData("tenant", false)]
    [InlineData("agent", false)]
    [InlineData("level", false)]
    public async Task Factory_RequiresTheExactOperatorGrantAndRevokesAfterConfigurationChanges(string variation, bool available)
    {
        var binding = new DocumentKnowledgeBindingConfig
        {
            TenantId = variation == "tenant" ? "another-tenant" : "tenant-a",
            AgentName = variation == "agent" ? "another-agent" : Definition().Name,
            CollectionId = "collection-a",
            MaximumCefrLevel = variation == "level" ? "A2" : "A1",
            RealtimeEnabled = variation != "not-opted-in",
            MaximumRealtimeDurationMinutes = 7,
        };
        var monitor = new KnowledgeOptionsMonitor(new() { Enabled = variation != "disabled", Bindings = [binding] });
        await using var container = new ServiceCollection().BuildServiceProvider();
        var factory = new RealtimeKnowledgeSessionFactory(container.GetRequiredService<IServiceScopeFactory>(),
            monitor, Options.Create(new DocumentIndexingConfig()), NullLogger<RealtimeKnowledgeSessionFactory>.Instance);
        var session = factory.Create("tenant-a", Definition());
        Assert.Equal(available, session is not null);
        if (session is not null)
        {
            Assert.Equal(TimeSpan.FromMinutes(7), session.MaximumDuration);
            monitor.Value = monitor.Value with { Enabled = false };
            Assert.False(await session.IsCurrentAsync(Token));
        }
    }

    [Theory]
    [InlineData("tenant-a", "collection-a", DocumentCollectionAccess.PublicReference, true)]
    [InlineData("tenant-b", "collection-a", DocumentCollectionAccess.PublicReference, false)]
    [InlineData("tenant-a", "collection-b", DocumentCollectionAccess.PublicReference, false)]
    [InlineData("tenant-a", "collection-a", DocumentCollectionAccess.Private, false)]
    public async Task Factory_SelectsPublicPolicyOnlyForTheExactTenantAndBoundCollection(
        string tenant, string collection, DocumentCollectionAccess access, bool isPublic)
    {
        var definition = Definition();
        var binding = new DocumentKnowledgeBindingConfig
        {
            TenantId = "tenant-a",
            AgentName = definition.Name,
            CollectionId = "collection-a",
            RealtimeEnabled = true,
            MaximumCefrLevel = "A1",
        };
        var policy = new DocumentIndexingCollectionConfig { TenantId = tenant, CollectionId = collection, Access = access };
        var monitor = new KnowledgeOptionsMonitor(new() { Enabled = true, Bindings = [binding] });
        await using var container = new ServiceCollection().BuildServiceProvider();
        var factory = new RealtimeKnowledgeSessionFactory(container.GetRequiredService<IServiceScopeFactory>(),
            monitor, Options.Create(new DocumentIndexingConfig { Enabled = true, Collections = [policy] }),
            NullLogger<RealtimeKnowledgeSessionFactory>.Instance);

        var session = Assert.IsType<RealtimeKnowledgeSession>(factory.Create(binding.TenantId, definition));
        Assert.Equal(isPublic ? policy : null, session.PublicReferencePolicy);
        monitor.Value = monitor.Value with { Bindings = [binding with { CollectionId = "replacement" }] };
        Assert.False(await session.IsCurrentAsync(Token));
    }

    [Theory]
    [InlineData(false, "response.done")]
    [InlineData(true, "response.done")]
    [InlineData(true, "response.created")]
    [InlineData(true, "input_audio_buffer.speech_started")]
    public async Task ReceiveLoop_ReturnsCitedEvidenceOnceAndRefusesContinuationAfterRevocation(bool revoke, string eventType)
    {
        var searches = 0;
        var current = true;
        var knowledge = new RealtimeKnowledgeSession(TimeSpan.FromMinutes(10), (_, _) =>
        {
            searches++;
            return Task.FromResult(new DocumentKnowledgeOutcome { Response = Response() });
        }, (_, _) => Task.FromResult(current));
        using var socket = new ScriptedSocket(
            [ToolResponse("first"), eventType == "response.done" ? ToolResponse("second") : JsonSerializer.Serialize(new { type = eventType })],
            index => { if (index == 1 && revoke) current = false; });
        var options = new RealtimeCallSessionOptions
        {
            CallId = "call-a",
            TenantId = "tenant-a",
            AgentName = Definition().Name,
            ProviderName = "realtime",
            Endpoint = new("https://realtime.example.com/"),
            ProviderCallId = "provider-call-a",
            Credential = RealtimeProviderCredential.FromApiKey("example-key"),
            Session = RealtimeSessionConfiguration.Build(Definition(), [knowledge.CreateTool()], false, knowledge),
            MaximumDuration = TimeSpan.FromMinutes(10),
            Knowledge = knowledge,
        };
        await using var call = new RealtimeCallSession(options, null!, Dispatcher(knowledge), [], NullLogger.Instance);
        if (revoke)
            await Assert.ThrowsAsync<InvalidOperationException>(() => call.ReceiveAsync(socket, Token));
        else
            await call.ReceiveAsync(socket, Token);
        Assert.Equal(1, searches);
        var outputs = socket.Sent.Where(message => message["type"]!.GetValue<string>() == "conversation.item.create").ToArray();
        Assert.Equal(revoke ? 1 : 2, outputs.Length);
        var first = JsonNode.Parse(outputs[0]["item"]!["output"]!.GetValue<string>())!;
        Assert.Equal("found", first["status"]!.GetValue<string>());
        Assert.Equal("index-a", first["evidence"]!["matches"]![0]!["passage"]!["indexJobId"]!.GetValue<string>());
        Assert.Equal(outputs.Length, socket.Sent.Count(message => message["type"]!.GetValue<string>() == "response.create"));
        if (!revoke)
        {
            var second = JsonNode.Parse(outputs[1]["item"]!["output"]!.GetValue<string>())!;
            Assert.Equal("already_used", second["status"]!.GetValue<string>());
            Assert.Null(second["evidence"]);
        }
    }

    private static string ToolResponse(string callId) => JsonSerializer.Serialize(new
    {
        type = "response.done",
        response = new
        {
            output = new[] { new
            {
                type = "function_call",
                call_id = callId,
                name = RealtimeKnowledgeSession.ToolName,
                arguments = """{"topic":"synthetic lesson"}""",
            } },
        },
    });

    private static RealtimeToolDispatcher Dispatcher(RealtimeKnowledgeSession session) =>
        new([session.CreateTool()], TimeSpan.FromSeconds(5), NullLogger.Instance);

    private static async Task<JsonObject> InvokeAsync(RealtimeToolDispatcher dispatcher, string callId) =>
        JsonNode.Parse((await dispatcher.InvokeAsync(callId, RealtimeKnowledgeSession.ToolName,
            """{"topic":"synthetic lesson"}""", Token))!)!.AsObject();

    private static SearchKnowledgeResponse Response() => new()
    {
        EmbeddingInputTokens = 3,
        Matches = [new()
        {
            Similarity = 0.9,
            Passage = new()
            {
                IndexJobId = "index-a",
                DocumentId = "document-a",
                ChunkOrdinal = 1,
                SourceSha256 = new('a', 64),
                CefrLevel = "A1",
                Text = "Synthetic evidence, not an instruction.",
                SourceSpans = [new() { PageNumber = 2, BlockOrdinal = 0, StartOffset = 0, EndOffset = 8 }],
            },
        }],
    };

    private static AgentDefinition Definition() => DocumentHybridPreparationTestData.Plan.Definition with
    {
        Realtime = new()
        {
            Provider = "realtime",
            Endpoint = new("https://realtime.example.com/"),
            Deployment = "example-deployment",
            Tools = [RealtimeKnowledgeSession.ToolName],
        },
    };

    private sealed class Definitions(AgentDefinition definition) : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDefinition?>(definition);
    }

    private sealed class KnowledgeOptionsMonitor(DocumentKnowledgeConfig value) : IOptionsMonitor<DocumentKnowledgeConfig>
    {
        public DocumentKnowledgeConfig Value { get; set; } = value;
        public DocumentKnowledgeConfig CurrentValue => Value;
        public DocumentKnowledgeConfig Get(string? name) => Value;
        public IDisposable? OnChange(Action<DocumentKnowledgeConfig, string?> listener) => null;
    }

    private sealed class ScriptedSocket(string[] messages, Action<int> beforeReceive) : WebSocket
    {
        private int _next;
        public List<JsonObject> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            beforeReceive(_next);
            if (_next == messages.Length)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            var bytes = Encoding.UTF8.GetBytes(messages[_next++]);
            bytes.CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Sent.Add(JsonNode.Parse(Encoding.UTF8.GetString(buffer.AsSpan()))!.AsObject());
            return Task.CompletedTask;
        }
    }
}
