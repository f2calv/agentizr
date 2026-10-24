using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace CasCap.IntegrationTests;

/// <summary>Runs the real host with an in-memory immutable definition authority.</summary>
public class AgentDefinitionAdministrationWebApplicationFactory : AgentizrWebApplicationFactory
{
    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CasCap:AgentDefinitionPolicyConfig:AllowedMcpAuthorities:0"] = "https://mcp.example.com",
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentDefinitionStore>();
            services.RemoveAll<IAgentDefinitionAdministrationStore>();
            services.RemoveAll<IEvaluationAgentFactory>();
            services.AddSingleton<IEvaluationAgentFactory>(
                new ScriptedEvaluationAgentFactory(ScriptedEvaluationAgentFactory.Answer("The answer is 42.")));
            services.AddSingleton<TestDefinitionStore>();
            services.AddSingleton<IAgentDefinitionStore>(serviceProvider =>
                serviceProvider.GetRequiredService<TestDefinitionStore>());
            services.AddSingleton<IAgentDefinitionAdministrationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<TestDefinitionStore>());
        });
    }

    private sealed class TestDefinitionStore : IAgentDefinitionStore, IAgentDefinitionAdministrationStore
    {
        private sealed class NoOpLock : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

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

        public ValueTask<IAsyncDisposable> AcquireTenantActivationLockAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable>(new NoOpLock());

        public ValueTask<bool> ActivateAsync(
            string agentName,
            string definitionVersion,
            string? activatedBy,
            string? changeReason,
            CancellationToken cancellationToken,
            ExpectedActiveDefinition? expectedActive = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (!_snapshots.ContainsKey((agentName, definitionVersion)))
                    return ValueTask.FromResult(false);
                EnsureExpectedActive(agentName, expectedActive);
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

        public ValueTask<bool> DeactivateAsync(
            string agentName,
            string? deactivatedBy,
            string changeReason,
            CancellationToken cancellationToken,
            ExpectedActiveDefinition? expectedActive = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                EnsureExpectedActive(agentName, expectedActive);
                if (!_activeVersions.Remove(agentName, out var version))
                    return ValueTask.FromResult(false);
                _activations.Add((agentName, new AgentDefinitionActivationItem
                {
                    DefinitionVersion = version,
                    ActivatedAtUtc = new DateTimeOffset(2026, 10, 7, 1, _activations.Count, 0, TimeSpan.Zero),
                    ActivatedBy = deactivatedBy,
                    ChangeReason = changeReason,
                    IsDeactivation = true,
                }));
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
                return ValueTask.FromResult<IReadOnlyList<AgentDefinition>>(
                    _activeVersions.Select(pair => _snapshots[(pair.Key, pair.Value)].Definition).ToArray());
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

        public ValueTask<AgentDefinitionSummaryPage> GetAgentsAsync(
            string? nameFilter,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                var agents = _snapshots.Values
                    .GroupBy(snapshot => snapshot.Definition.Name)
                    .Where(group => nameFilter is null || group.Key.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToArray();
                return ValueTask.FromResult(new AgentDefinitionSummaryPage(
                    [.. agents.Skip(skip).Take(take).Select(group =>
                    {
                        var latest = group.MaxBy(snapshot => snapshot.PublishedAtUtc)!;
                        return new AgentDefinitionSummaryItem
                        {
                            AgentName = group.Key,
                            ActiveDefinition = _activeVersions.TryGetValue(group.Key, out var version)
                                ? _snapshots[(group.Key, version)].Definition
                                : null,
                            LatestVersion = latest.Definition.Version,
                            LatestPublishedAtUtc = latest.PublishedAtUtc,
                            LatestPublishedBy = latest.PublishedBy,
                            VersionCount = group.Count(),
                        };
                    })],
                    agents.Length));
            }
        }

        public ValueTask<IReadOnlyDictionary<string, int>> CountAgentsAsync(
            IReadOnlyCollection<string> tenantIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                var count = _snapshots.Keys.Select(key => key.AgentName).Distinct(StringComparer.Ordinal).Count();
                return ValueTask.FromResult<IReadOnlyDictionary<string, int>>(
                    tenantIds.ToDictionary(tenantId => tenantId, _ => count, StringComparer.Ordinal));
            }
        }

        private void EnsureExpectedActive(string agentName, ExpectedActiveDefinition? expectedActive)
        {
            if (expectedActive is not null && expectedActive.Version != _activeVersions.GetValueOrDefault(agentName))
                throw new CasCap.Exceptions.AgentDefinitionActiveVersionConflictException();
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
