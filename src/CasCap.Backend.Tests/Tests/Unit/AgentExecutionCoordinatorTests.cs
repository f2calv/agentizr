using CasCap.Exceptions;
using Microsoft.AspNetCore.Http;
using System.Diagnostics.Metrics;

namespace CasCap.Tests.Unit;

/// <summary>Tests tenant-owned execution and stateless session-control policy.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentExecutionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_LoadsAndPersistsSessionState()
    {
        var sessionStore = new RecordingSessionStore("before");
        var executor = new RecordingExecutor(new AgentExecutionResult
        {
            OutputText = "done",
            SessionStateJson = "after",
        });
        var coordinator = CreateCoordinator(sessionStore, new AgentOverrideState(), executor);
        var request = new AgentExecutionRequest { AgentName = "assistant", SessionId = "session", Input = "hello" };

        var result = await coordinator.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("done", result.OutputText);
        Assert.Equal("before", executor.Context?.SessionStateJson);
        Assert.Equal("after", sessionStore.SavedState);
        Assert.Equal("secret", executor.Context?.ProviderApiKey);
        Assert.Null(executor.Context?.Definition.Provider.ApiKey);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledSessionBypassesState()
    {
        var sessionStore = new RecordingSessionStore("before");
        var executor = new RecordingExecutor(new AgentExecutionResult
        {
            OutputText = "done",
            SessionStateJson = "after",
        });
        var coordinator = CreateCoordinator(
            sessionStore,
            new AgentOverrideState { SessionEnabled = false },
            executor);
        var request = new AgentExecutionRequest { AgentName = "assistant", SessionId = "session", Input = "hello" };

        await coordinator.ExecuteAsync(request, CancellationToken.None);

        Assert.Null(executor.Context?.SessionStateJson);
        Assert.Equal(0, sessionStore.GetCount);
        Assert.Null(sessionStore.SavedState);
    }

    [Fact]
    public async Task ExecuteAsync_RequestBypassSkipsSessionState()
    {
        var sessionStore = new RecordingSessionStore("before");
        var executor = new RecordingExecutor(new AgentExecutionResult
        {
            OutputText = "done",
            SessionStateJson = "after",
        });
        var coordinator = CreateCoordinator(sessionStore, new AgentOverrideState(), executor);
        var request = new AgentExecutionRequest
        {
            AgentName = "assistant",
            SessionId = "session",
            Input = "hello",
            BypassSession = true,
        };

        await coordinator.ExecuteAsync(request, CancellationToken.None);

        Assert.Null(executor.Context?.SessionStateJson);
        Assert.Equal(0, sessionStore.GetCount);
        Assert.Null(sessionStore.SavedState);
    }

    [Fact]
    public async Task StatelessDefinitionIgnoresOverridesAndSessionState()
    {
        var sessionStore = new RecordingSessionStore("before");
        var executor = new RecordingExecutor(new AgentExecutionResult { OutputText = "done", SessionStateJson = "after" });
        var coordinator = CreateCoordinator(sessionStore,
            new AgentOverrideState { ModelName = "unapproved-model", Instructions = "unapproved instructions" },
            executor, stateless: true);

        var result = await coordinator.ExecuteAsync(
            new AgentExecutionRequest { AgentName = "preparation", SessionId = "job", Input = "page" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(executor.Context?.Overrides.ModelName);
        Assert.Null(executor.Context?.Overrides.Instructions);
        Assert.Null(executor.Context?.SessionStateJson);
        Assert.Null(executor.Context?.HistorySink);
        Assert.Equal(0, sessionStore.GetCount);
        Assert.Null(sessionStore.SavedState);
        Assert.Equal("test-model", result.ModelName);
    }

    [Fact]
    public async Task StatelessSessionReadsReportEffectivePolicy()
    {
        var sessions = new RecordingSessionStore("before");
        var control = CreateStatelessControl(sessions);
        var cancellation = TestContext.Current.CancellationToken;

        var status = await control.GetStatusAsync("preparation", "job", cancellation);
        var overrides = await control.GetOverridesAsync("preparation", "job", cancellation);
        var instructions = await control.GetInstructionsAsync("preparation", "job", cancellation);

        Assert.True(status.AgentExists);
        Assert.False(status.SessionEnabled);
        Assert.False(status.SessionExists);
        Assert.NotNull(overrides);
        Assert.False(overrides.SessionEnabled);
        Assert.Null(overrides.ModelName);
        Assert.Null(overrides.Instructions);
        Assert.NotNull(instructions);
        Assert.False(instructions.Overridden);
        Assert.Equal("fixed instructions", instructions.Instructions);
        Assert.Equal(0, sessions.GetCount);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("compact")]
    [InlineData("save")]
    [InlineData("load")]
    [InlineData("delete")]
    [InlineData("overrides")]
    public async Task StatelessSessionMutationsAreRejected(string operation)
    {
        var sessions = new RecordingSessionStore("before");
        var control = CreateStatelessControl(sessions);
        var cancellation = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<StatelessAgentSessionException>(async () =>
        {
            switch (operation)
            {
                case "reset": await control.ResetAsync("preparation", "job", cancellation); break;
                case "compact": await control.CompactAsync("preparation", "job", 1, cancellation); break;
                case "save": await control.SaveSnapshotAsync("preparation", "job", "snapshot", cancellation); break;
                case "load": await control.LoadSnapshotAsync("preparation", "job", "snapshot", cancellation); break;
                case "delete": await control.DeleteSnapshotAsync("preparation", "job", "snapshot", cancellation); break;
                case "overrides": await control.SetOverridesAsync("preparation", "job", new AgentOverrideState(), cancellation); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });
        Assert.Equal(0, sessions.GetCount);
        Assert.Null(sessions.SavedState);
    }

    [Fact]
    public async Task StatelessSessionPolicyReturnsHttpConflict()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        using var provider = services.BuildServiceProvider();
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = body;

        var handled = await new AgentSessionConflictHandler().TryHandleAsync(
            context, new StatelessAgentSessionException(), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        body.Position = 0;
        using var problem = await System.Text.Json.JsonDocument.ParseAsync(body,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Session unavailable", problem.RootElement.GetProperty("title").GetString());
    }

    private static AgentSessionControlService CreateStatelessControl(RecordingSessionStore sessions) =>
        new(new StaticDefinitionStore(stateless: true), new StaticCredentialStore(), sessions,
            new StaticOverrideStore(new AgentOverrideState(), failOnRead: true), new StaticSessionCodec(),
            Options.Create(new AgentRuntimeConfig
            {
                Tenants = new Dictionary<string, TenantAgentConfig>
                {
                    ["tenant"] = new() { InstructionsDisclosureEnabled = true },
                },
            }), new StaticTenantContext(),
            new ConfiguredAgentSessionLock(new InMemoryAgentSessionLock(), new StaticTenantContext()));

    private static AgentExecutionCoordinator CreateCoordinator(
        RecordingSessionStore sessionStore,
        AgentOverrideState overrides,
        RecordingExecutor executor,
        bool stateless = false) =>
        new(
            new AgentRuntimeMetrics(Options.Create(new AppConfig()), new TestMeterFactory()),
            new StaticTenantContext(),
            new StaticDefinitionStore(stateless),
            new StaticCredentialStore(),
            sessionStore,
            new StaticOverrideStore(overrides, failOnRead: stateless),
            new StaticSessionCodec(),
            executor,
            new ConfiguredAgentSessionLock(new InMemoryAgentSessionLock(), new StaticTenantContext()));

    private sealed class StaticTenantContext : ITenantContext
    {
        public string TenantId => "tenant";
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }

    private sealed class StaticDefinitionStore(bool stateless = false) : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDefinition?>(new AgentDefinition
            {
                Name = agentName,
                Version = "1",
                Agent = new AgentConfig
                {
                    Provider = "provider",
                    Name = agentName,
                    Description = "Test agent",
                    Prompt = "Test prompt",
                    Instructions = "fixed instructions",
                    Stateless = stateless,
                },
                Provider = new ProviderConfig
                {
                    Type = AgentType.Ollama,
                    ModelName = "test-model",
                    ApiKey = null,
                },
            });

        public ValueTask PublishAsync(
            AgentDefinition definition,
            int schemaVersion,
            string? publishedBy,
            string? changeReason,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
            string agentName,
            int limit,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AgentDefinitionHistoryItem>>([]);
    }

    private sealed class StaticCredentialStore : IProviderCredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("secret");

        public Azure.Core.TokenCredential? GetTokenCredential(string providerName) => null;
    }

    private sealed class RecordingSessionStore(string? initialState) : IAgentSessionStore
    {
        public int GetCount { get; private set; }

        public string? SavedState { get; private set; }

        public ValueTask<string?> GetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            CancellationToken cancellationToken)
        {
            GetCount++;
            return ValueTask.FromResult(initialState);
        }

        public ValueTask SetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            string sessionStateJson,
            CancellationToken cancellationToken)
        {
            SavedState = sessionStateJson;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<string?> GetSnapshotAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            string snapshotName,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask SetSnapshotAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            string snapshotName,
            string sessionStateJson,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteSnapshotAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            string snapshotName,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class StaticOverrideStore(AgentOverrideState state, bool failOnRead = false) : IAgentOverrideStore
    {
        public ValueTask<AgentOverrideState> GetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            CancellationToken cancellationToken) =>
            failOnRead
                ? throw new InvalidOperationException("Stateless execution must not read session overrides.")
                : ValueTask.FromResult(state);

        public ValueTask SetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            AgentOverrideState stateValue,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class StaticSessionCodec : IAgentSessionCodec
    {
        public ValueTask<AgentSessionInspection> InspectAsync(
            AgentDefinition definition,
            string? providerApiKey,
            string sessionStateJson,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentSessionInspection { SizeBytes = sessionStateJson.Length });

        public ValueTask<AgentSessionCompactionResult> CompactAsync(
            AgentDefinition definition,
            string? providerApiKey,
            string sessionStateJson,
            int retainMessageCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingExecutor(AgentExecutionResult result) : IAgentExecutor
    {
        public AgentExecutionContext? Context { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            return ValueTask.FromResult(result);
        }
    }
}
