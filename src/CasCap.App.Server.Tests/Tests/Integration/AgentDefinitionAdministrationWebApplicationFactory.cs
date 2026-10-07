using Microsoft.AspNetCore.TestHost;

namespace CasCap.IntegrationTests;

/// <summary>Runs the real host with an in-memory immutable definition authority.</summary>
public sealed class AgentDefinitionAdministrationWebApplicationFactory : AgentizrWebApplicationFactory
{
    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentDefinitionStore>();
            services.RemoveAll<IAgentDefinitionAdministrationStore>();
            services.AddSingleton<TestDefinitionStore>();
            services.AddSingleton<IAgentDefinitionStore>(serviceProvider =>
                serviceProvider.GetRequiredService<TestDefinitionStore>());
            services.AddSingleton<IAgentDefinitionAdministrationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<TestDefinitionStore>());
        });
    }

    private sealed class TestDefinitionStore : IAgentDefinitionStore, IAgentDefinitionAdministrationStore
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<(string AgentName, string Version), AgentDefinitionSnapshotItem> _snapshots = [];
        private readonly Dictionary<string, string> _activeVersions = new(StringComparer.Ordinal);
        private readonly List<(string AgentName, AgentDefinitionActivationItem Item)> _activations = [];

        public TestDefinitionStore()
        {
            var definition = CreateDefinition("test-v1", "test-model");
            _snapshots[(definition.Name, definition.Version)] = new AgentDefinitionSnapshotItem
            {
                Definition = definition,
                SchemaVersion = 1,
                PublishedAtUtc = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
                PublishedBy = "bootstrap",
                ChangeReason = "test bootstrap",
                IsActive = true,
            };
            _activeVersions[definition.Name] = definition.Version;
        }

        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                return ValueTask.FromResult(_activeVersions.TryGetValue(agentName, out var version)
                    ? _snapshots[(agentName, version)].Definition
                    : null);
        }

        public ValueTask PublishAsync(
            AgentDefinition definition,
            int schemaVersion,
            string? publishedBy,
            string? changeReason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                var key = (definition.Name, definition.Version);
                if (_snapshots.ContainsKey(key))
                    throw new CasCap.Exceptions.AgentDefinitionVersionConflictException();
                _snapshots[key] = new AgentDefinitionSnapshotItem
                {
                    Definition = definition,
                    SchemaVersion = schemaVersion,
                    PublishedAtUtc = new DateTimeOffset(2026, 10, 7, 0, _snapshots.Count, 0, TimeSpan.Zero),
                    PublishedBy = publishedBy,
                    ChangeReason = changeReason,
                };
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
            string agentName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                return ValueTask.FromResult(_activeVersions.TryGetValue(agentName, out var version)
                    ? WithActive(_snapshots[(agentName, version)], true)
                    : null);
        }

        public ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
            string agentName,
            string definitionVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                return ValueTask.FromResult(_snapshots.TryGetValue((agentName, definitionVersion), out var snapshot)
                    ? WithActive(snapshot, _activeVersions.GetValueOrDefault(agentName) == definitionVersion)
                    : null);
        }

        public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
            string agentName,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                return ValueTask.FromResult<IReadOnlyList<AgentDefinitionHistoryItem>>(
                    _snapshots
                        .Where(pair => pair.Key.AgentName == agentName)
                        .Select(pair => pair.Value)
                        .OrderByDescending(snapshot => snapshot.PublishedAtUtc)
                        .Take(limit)
                        .Select(snapshot => new AgentDefinitionHistoryItem
                        {
                            DefinitionVersion = snapshot.Definition.Version,
                            SchemaVersion = snapshot.SchemaVersion,
                            PublishedAtUtc = snapshot.PublishedAtUtc,
                            PublishedBy = snapshot.PublishedBy,
                            ChangeReason = snapshot.ChangeReason,
                            IsActive = snapshot.Definition.Version == _activeVersions.GetValueOrDefault(agentName),
                        })
                        .ToArray());
            }
        }

        public ValueTask<bool> ActivateAsync(
            string agentName,
            string definitionVersion,
            string? activatedBy,
            string? changeReason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (!_snapshots.ContainsKey((agentName, definitionVersion)))
                    return ValueTask.FromResult(false);
                _activeVersions[agentName] = definitionVersion;
                _activations.Add((agentName, new AgentDefinitionActivationItem
                {
                    DefinitionVersion = definitionVersion,
                    ActivatedAtUtc = new DateTimeOffset(2026, 10, 7, 1, _activations.Count, 0, TimeSpan.Zero),
                    ActivatedBy = activatedBy,
                    ChangeReason = changeReason,
                }));
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
            string agentName,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                return ValueTask.FromResult<IReadOnlyList<AgentDefinitionActivationItem>>(
                    _activations.AsEnumerable()
                        .Reverse()
                        .Where(activation => activation.AgentName == agentName)
                        .Take(limit)
                        .Select(activation => activation.Item)
                        .ToArray());
        }

        private static AgentDefinitionSnapshotItem WithActive(AgentDefinitionSnapshotItem snapshot, bool isActive) =>
            snapshot with { IsActive = isActive };

        private static AgentDefinition CreateDefinition(string version, string modelName) => new()
        {
            Name = "assistant",
            Version = version,
            Agent = new AgentConfig
            {
                Provider = "test",
                Name = "assistant",
                Description = "Test assistant",
                Prompt = "Test prompt",
            },
            Provider = new ProviderConfig
            {
                Type = AgentType.Ollama,
                ModelName = modelName,
                Endpoint = new Uri("http://localhost:11434"),
            },
        };
    }
}
