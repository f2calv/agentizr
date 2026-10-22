using CasCap.Constants;
using CasCap.IntegrationTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;

namespace CasCap.Tests.Integration;

/// <summary>Checks durable turn outcomes, partial activity, interruption recovery and history privacy.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
public sealed class AgentRunHistoryTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private readonly ServiceProvider _metricServices = new ServiceCollection()
        .AddMetrics().AddOptions().AddSingleton<AgentRuntimeMetrics>().BuildServiceProvider();

    private AgentRuntimeMetrics Metrics => _metricServices.GetRequiredService<AgentRuntimeMetrics>();

    /// <inheritdoc/>
    public void Dispose() => _metricServices.Dispose();

    [Theory]
    [InlineData(AgentRunStatuses.Completed, "completed")]
    [InlineData(AgentRunStatuses.Failed, "failed")]
    [InlineData(AgentRunStatuses.Cancelled, "cancelled")]
    public async Task History_PreservesMessagesToolsAndTerminalOutcome(string expectedStatus, string expectedOutcome)
    {
        var measurements = new List<(string Operation, string Outcome, long Count)>();
        using var listener = Listen(measurements);
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var history = CreateHistory(tenant);
        history.ExcludeSecret("synthetic-private-key");
        var id = Assert.IsType<Guid>(await history.StartAsync(CreateRequest(), CreateDefinition(), "model", TestContext.Current.CancellationToken));
        await history.AppendAsync(id, new AgentRunActivity
        {
            Kind = "tool-completed",
            AgentName = "assistant",
            CallId = "call-1",
            ToolName = "test",
            Data = new
            {
                Result = "synthetic-private-key",
                Authorization = "must-not-appear",
                Password = "must-not-appear",
                Content = new[] { new { type = "image", data = "AQID" } },
                Bytes = new byte[] { 1, 2, 3 },
                Nested = new { Text = "keep this result" },
            },
        }, TestContext.Current.CancellationToken);
        Exception? error = expectedStatus switch
        {
            AgentRunStatuses.Failed => new InvalidOperationException("must-not-appear"),
            AgentRunStatuses.Cancelled => new OperationCanceledException(),
            _ => null,
        };
        await history.FinishAsync(id, error is null ? new AgentExecutionResult { OutputText = "answer" } : null, error);

        await using var db = await fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var run = await db.AgentRuns.SingleAsync(run => run.Id == id, TestContext.Current.CancellationToken);
        var activities = await db.AgentRunActivities.Where(activity => activity.RunId == id).OrderBy(activity => activity.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedStatus, run.Status);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(error?.GetType().FullName, run.ErrorType);
        Assert.Equal(2, activities.Count);
        var content = string.Join("\n", activities.Select(activity => activity.ActivityJson));
        Assert.Contains("hello", content, StringComparison.Ordinal);
        Assert.Contains("keep this result", content, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-key", content, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-appear", content, StringComparison.Ordinal);
        Assert.DoesNotContain("AQID", content, StringComparison.Ordinal);
        if (error is null)
            Assert.Contains("answer", run.SummaryJson!, StringComparison.Ordinal);
        Assert.Contains(("history.start", "started", 1L), measurements);
        Assert.Contains(("history.activity", "appended", 1L), measurements);
        Assert.Contains(("history.finish", expectedOutcome, 1L), measurements);
    }

    [Fact]
    public async Task History_NewOwnerMarksInterruptedAndResetDoesNotDeleteArchive()
    {
        var measurements = new List<(string Operation, string Outcome, long Count)>();
        using var listener = Listen(measurements);
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var history = CreateHistory(tenant);
        var state = new PostgresAgentStateStore(NullLogger<PostgresAgentStateStore>.Instance,
            Options.Create(new AgentRuntimeConfig()), Options.Create(new CachingConfig()),
            TimeProvider.System, Metrics, fixture.DbContextFactory, tenant);
        Guid priorId;
        await using (await state.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
        {
            priorId = Assert.IsType<Guid>(await history.StartAsync(CreateRequest(), CreateDefinition(), "model", TestContext.Current.CancellationToken));
            await new PostgresAgentSessionStore(state, tenant).SetAsync(
                "assistant", "v1", "session", "state", TestContext.Current.CancellationToken);
        }
        await using (await state.AcquireAsync("assistant", "session", TestContext.Current.CancellationToken))
        {
            await history.StartAsync(CreateRequest(), CreateDefinition(), "model", TestContext.Current.CancellationToken);
            await new PostgresAgentSessionStore(state, tenant).DeleteAsync(
                "assistant", "v1", "session", TestContext.Current.CancellationToken);
        }
        await using var db = await fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatuses.Interrupted, (await db.AgentRuns.SingleAsync(
            run => run.Id == priorId, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, await db.AgentRuns.CountAsync(run => run.TenantId == tenant.TenantId, TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.AgentRunActivities.CountAsync(activity =>
            db.AgentRuns.Any(run => run.Id == activity.RunId && run.TenantId == tenant.TenantId), TestContext.Current.CancellationToken));
        Assert.Contains(("history.recover", "interrupted", 1L), measurements);
    }

    [Fact]
    public async Task History_DisabledByDefault()
    {
        var tenant = new StaticTenantContext(Guid.NewGuid().ToString("N"));
        var history = new AgentRunHistoryService(Options.Create(new AgentRuntimeConfig()),
            TimeProvider.System, fixture.DbContextFactory, tenant, Metrics);
        Assert.Null(await history.StartAsync(CreateRequest(), CreateDefinition(), "model", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, AgentRunStatuses.Failed)]
    [InlineData(true, AgentRunStatuses.Cancelled)]
    public async Task Coordinator_ArchivesPartialActivityWhenExecutionFailsOrCallerCancels(bool cancel, string expectedStatus)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var root = new AgentizrWebApplicationFactory();
        using var host = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped(provider => CreateHistory(provider.GetRequiredService<ITenantContext>()));
            services.RemoveAll<IAgentExecutor>();
            services.AddSingleton<IAgentExecutor>(new FaultingExecutor(cancel ? cancellation : null));
        }));
        using var scope = host.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AgentExecutionCoordinator>();
        var request = CreateRequest() with { SessionId = Guid.NewGuid().ToString("N") };
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await coordinator.ExecuteAsync(request, cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await coordinator.ExecuteAsync(request, cancellation.Token));
        await using var db = await fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var run = await db.AgentRuns.SingleAsync(run => run.SessionId == request.SessionId, TestContext.Current.CancellationToken);
        Assert.Equal(expectedStatus, run.Status);
        Assert.Equal(2, await db.AgentRunActivities.CountAsync(activity => activity.RunId == run.Id, TestContext.Current.CancellationToken));
    }

    private MeterListener Listen(List<(string Operation, string Outcome, long Count)> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Name == "agentizr.ha.operations"
                    && ReferenceEquals(instrument.Meter.Scope, _metricServices.GetRequiredService<IMeterFactory>()))
                    current.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var dimensions = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            Assert.Equal(["operation", "outcome", "tenant"], dimensions.Keys.Order().ToArray());
            measurements.Add(((string)dimensions["operation"]!, (string)dimensions["outcome"]!, value));
        });
        listener.Start();
        return listener;
    }

    private AgentRunHistoryService CreateHistory(ITenantContext tenant) => new(
        Options.Create(new AgentRuntimeConfig { ConversationHistoryEnabled = true }),
        TimeProvider.System, fixture.DbContextFactory, tenant, Metrics);

    private static AgentExecutionRequest CreateRequest() => new()
    {
        AgentName = "assistant",
        SessionId = "session",
        Input = "hello",
        BinaryContent = [1, 2, 3],
    };

    private static AgentDefinition CreateDefinition() => new()
    {
        Name = "assistant",
        Version = "v1",
        Agent = new AgentConfig { Name = "assistant", Provider = "test", Description = "d", Prompt = "p" },
        Provider = new ProviderConfig { Type = AgentType.Ollama, ModelName = "model", Endpoint = new Uri("http://localhost:11434") },
    };

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class FaultingExecutor(CancellationTokenSource? cancellation) : IAgentExecutor
    {
        public async ValueTask<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken)
        {
            Assert.NotNull(context.HistorySink);
            await context.HistorySink(new AgentRunActivity
            {
                Kind = "tool-started",
                AgentName = context.Request.AgentName,
                CallId = "call-1",
                ToolName = "test",
            }, cancellationToken);
            if (cancellation is not null)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("must-not-be-archived");
        }
    }
}
