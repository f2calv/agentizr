namespace CasCap.Tests.Unit;

/// <summary>Tests tenant-owned execution orchestration around a low-level executor.</summary>
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

    private static AgentExecutionCoordinator CreateCoordinator(
        RecordingSessionStore sessionStore,
        AgentOverrideState overrides,
        RecordingExecutor executor) =>
        new(
            new StaticDefinitionStore(),
            new StaticCredentialStore(),
            sessionStore,
            new StaticOverrideStore(overrides),
            executor);

    private sealed class StaticDefinitionStore : IAgentDefinitionStore
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
    }

    private sealed class StaticOverrideStore(AgentOverrideState state) : IAgentOverrideStore
    {
        public ValueTask<AgentOverrideState> GetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(state);

        public ValueTask SetAsync(
            string agentName,
            string definitionVersion,
            string sessionId,
            AgentOverrideState stateValue,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
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
