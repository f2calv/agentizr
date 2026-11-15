using CasCap.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;

namespace CasCap.Tests.Integration;

/// <summary>Verifies voice retention, caller isolation, exact expiry and active-call ownership on isolated PostgreSQL.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
public sealed class RealtimeConversationStoreTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Resume_AcrossScopesRestoresContextAndExcludesConcurrentAndForeignCallers()
    {
        var clock = new VoiceClock();
        await using var services = CreateServices(clock);
        var store = CreateStore(services, clock);
        var tenant = Guid.NewGuid().ToString("N");
        var caller = Caller("synthetic-owner");
        Guid id;
        await using (var first = await store.OpenAsync(tenant, "tutor", "v1", caller, null, Token))
        {
            Assert.NotNull(first);
            id = first.Id;
            await first.RecordAsync("item_user", "user", "Synthetic question", Token);
            await first.RecordAsync("item_assistant", "assistant", "Synthetic answer", Token);
            await Assert.ThrowsAsync<AgentSessionConflictException>(() =>
                CreateStore(services, clock).OpenAsync(tenant, "tutor", "v1", caller, id, Token));
        }
        await using var resumed = await CreateStore(services, clock).OpenAsync(tenant, "tutor", "v1", caller, id, Token);
        Assert.NotNull(resumed);
        Assert.Equal(2, resumed.Conversation.Replay().Count());
        Assert.Null(await store.OpenAsync(tenant, "tutor", "v1", Caller("other-owner"), id, Token));
        Assert.Null(await store.OpenAsync("other-tenant", "tutor", "v1", caller, id, Token));
        Assert.Null(await store.OpenAsync(tenant, "other-agent", "v1", caller, id, Token));
    }

    [Fact]
    public async Task Resume_ExpiresAtExactlyTwentyFourHoursAndPurgeDeletesOnlyExpiredVoiceState()
    {
        var clock = new VoiceClock();
        await using var services = CreateServices(clock);
        var store = CreateStore(services, clock);
        var tenant = Guid.NewGuid().ToString("N");
        var caller = Caller("synthetic-owner");
        Guid id;
        await using (var first = await store.OpenAsync(tenant, "tutor", "v1", caller, null, Token))
        {
            Assert.NotNull(first);
            id = first.Id;
            await first.RecordAsync("item_user", "user", "Synthetic question", Token);
        }
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        await using (var resumed = await store.OpenAsync(tenant, "tutor", "v1", caller, id, Token))
            Assert.NotNull(resumed);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(await store.OpenAsync(tenant, "tutor", "v1", caller, id, Token));

        await using var fresh = await store.OpenAsync(tenant, "tutor", "v1", caller, null, Token);
        Assert.NotNull(fresh);
        await store.PurgeAsync(Token);
        await using var db = await postgres.DbContextFactory.CreateDbContextAsync(Token);
        Assert.Equal(1, await db.AgentStates.CountAsync(state => state.TenantId == tenant, Token));
    }

    [Fact]
    public async Task Resume_RejectsDefinitionChangesAndDoesNotInventMissingContext()
    {
        var clock = new VoiceClock();
        await using var services = CreateServices(clock);
        var store = CreateStore(services, clock);
        var tenant = Guid.NewGuid().ToString("N");
        var caller = Caller("synthetic-owner");
        Guid id;
        await using (var first = await store.OpenAsync(tenant, "tutor", "v1", caller, null, Token))
        {
            Assert.NotNull(first);
            id = first.Id;
        }
        Assert.Null(await store.OpenAsync(tenant, "tutor", "v2", caller, id, Token));
        Assert.Null(await store.OpenAsync(tenant, "tutor", "v1", caller, Guid.NewGuid(), Token));
        Assert.Null(await store.OpenAsync(tenant, "tutor", "v1", caller, Guid.Empty, Token));
        await using var stillOriginal = await store.OpenAsync(tenant, "tutor", "v1", caller, id, Token);
        Assert.NotNull(stillOriginal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_WaitsForReplayAcknowledgementsAndRejectsProviderErrors(bool rejectHistory)
    {
        var clock = new VoiceClock();
        await using var services = CreateServices(clock);
        var tenant = Guid.NewGuid().ToString("N");
        await using var history = await CreateStore(services, clock)
            .OpenAsync(tenant, "tutor", "v1", Caller("synthetic-owner"), null, Token);
        Assert.NotNull(history);
        await history.RecordAsync("item_user", "user", "Synthetic question", Token);
        await history.RecordAsync("item_assistant", "assistant", "Synthetic answer", Token);
        using var httpClient = new HttpClient();
        await using var call = new RealtimeCallSession(new RealtimeCallSessionOptions
        {
            CallId = "synthetic-call",
            TenantId = tenant,
            AgentName = "tutor",
            ProviderName = "realtime",
            Endpoint = new Uri("https://example.com/"),
            ProviderCallId = "rtc_synthetic",
            Credential = RealtimeProviderCredential.FromApiKey("example-key"),
            Session = new JsonObject { ["type"] = "realtime" },
            MaximumDuration = TimeSpan.FromMinutes(1),
            Conversation = history,
        }, new AzureRealtimeClient(httpClient),
            new RealtimeToolDispatcher([], TimeSpan.FromSeconds(1), NullLogger.Instance),
            [], NullLogger.Instance);
        using var socket = new VoiceSocket(
            [
                """{"type":"session.updated"}""",
                """{"type":"conversation.item.added","item":{"id":"item_user","role":"user"}}""",
                rejectHistory ? """{"type":"error","error":{"code":"invalid_value"}}"""
                    : """{"type":"conversation.item.done","item":{"id":"item_assistant","role":"assistant"}}""",
            ],
            () => Assert.False(call.Ready.IsCompleted));
        await call.InitializeAsync(socket, Token);
        Assert.Equal("session.update", socket.Sent[0]["type"]!.GetValue<string>());
        Assert.Equal("output_text", socket.Sent[2]["item"]!["content"]![0]!["type"]!.GetValue<string>());
        Assert.False(call.Ready.IsCompleted);
        if (rejectHistory)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => call.ReceiveAsync(socket, Token));
            Assert.False(call.Ready.IsCompletedSuccessfully);
        }
        else
        {
            await call.ReceiveAsync(socket, Token);
            Assert.True(call.Ready.IsCompletedSuccessfully);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ServiceProvider CreateServices(TimeProvider clock) => new ServiceCollection()
        .AddLogging().AddMetrics().AddOptions()
        .AddSingleton(postgres.DbContextFactory)
        .AddSingleton(clock)
        .AddSingleton<AgentRuntimeMetrics>()
        .BuildServiceProvider();

    private static RealtimeConversationStore CreateStore(IServiceProvider services, TimeProvider clock) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), clock);

    private static ClaimsPrincipal Caller(string subject) =>
        new(new ClaimsIdentity([new Claim("iss", "https://example.com/issuer"), new Claim("sub", subject)], "synthetic"));

    private sealed class VoiceClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class VoiceSocket(string[] messages, Action beforeAcknowledgement) : WebSocket
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
            if (_next == messages.Length)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            beforeAcknowledgement();
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
