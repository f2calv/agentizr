#if DEBUG
using CasCap.Common.Configuration.PostgreSql;
using CasCap.AgentRuntime.Contracts.RuntimeConfiguration;
using CasCap.Common.Configuration.PostgreSql.Services;
using CasCap.IntegrationTests;
using CasCap.RuntimeConfiguration.Models;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Verifies the unpublished Debug soak through the real PostgreSQL, monitor and logging pipeline.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RuntimeLoggingTests(RuntimeLoggingPostgreSqlFixture postgres) : IClassFixture<RuntimeLoggingPostgreSqlFixture>
{
    [Theory]
    [InlineData(LogEventLevel.Information)]
    [InlineData(LogEventLevel.Debug)]
    public async Task Reload_ControlsPipelineAndForwardingWithoutReplacingHost(LogEventLevel startupMinimumLevel)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "RuntimeLoggingTests",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CasCap:RuntimeConfigurationConfig:Enabled"] = "true",
            ["CasCap:RuntimeConfigurationConfig:Application"] = $"test-{Guid.NewGuid():N}",
            ["CasCap:RuntimeConfigurationConfig:ApplicationDatabaseReuseEnabled"] = "true",
            ["CasCap:DatabaseConfig:ConnectionString"] = new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString)
            {
                Database = "deliberately_wrong_database",
            }.ConnectionString,
            ["CasCap:DatabaseConfig:DatabaseName"] = new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString).Database,
            ["CasCap:RuntimeConfigurationConfig:RefreshIntervalMs"] = "100",
            ["Serilog:MinimumLevel:Default"] = startupMinimumLevel.ToString(),
        });
        var levelSwitch = CasCap.Extensions.WebApplicationBuilderExtensions.AddAgentRuntimeLogging(builder);
        Assert.NotNull(levelSwitch);
        var sink = new CaptureSink();
        using var forwarded = new CaptureProvider();
        builder.InitializeSerilog(configureLogger: configuration =>
            configuration.MinimumLevel.ControlledBy(levelSwitch).WriteTo.Sink(sink));
        builder.Logging.AddProvider(forwarded);
        builder.Services.AddMemoryCache();
        await using var app = builder.Build();
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<RuntimeConfigurationDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken))
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var store = app.Services.GetRequiredService<PostgresRuntimeConfigurationStore<RuntimeLoggingControls>>();
        await store.PublishAsync(new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Warning }, 0,
            "test-operator", "Initial publication", cancellationToken: TestContext.Current.CancellationToken);
        var cache = app.Services.GetRequiredService<IMemoryCache>();
        var sentinel = Guid.NewGuid();
        cache.Set("sentinel", sentinel);
        app.MapGet("/sentinel", () => cache.Get<Guid>("sentinel"));
        CasCap.Extensions.WebApplicationExtensions.MapStatusEndpoint(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var probe = app.Services.GetRequiredService<RuntimeLoggingProbe>();
        var initial = probe.ReadStatus();
        Assert.Equal(startupMinimumLevel.ToString(), initial.StartupMinimumLevel);
        Assert.Equal("Warning", initial.EffectiveMinimumLevel);
        Assert.False(initial.ProbeDebugEnabled);
        Assert.True(initial.ApplyCount >= 1);
        Assert.NotNull(initial.LastAppliedAtUtc);
        var status = await client.GetFromJsonAsync<JsonElement>("/status", TestContext.Current.CancellationToken);
        Assert.Equal(2, status.EnumerateObject().Count());
        Assert.Equal("agentizr", status.GetProperty("service").GetString());
        var runtimeStatus = status.GetProperty("runtimeLogging");
        Assert.Equal(6, runtimeStatus.EnumerateObject().Count());
        Assert.Equal(initial.InstanceId, runtimeStatus.GetProperty("instanceId").GetGuid());
        Assert.Equal("Warning", runtimeStatus.GetProperty("effectiveMinimumLevel").GetString());
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CaptureProvider.Category);
        Assert.False(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug));
        await store.PublishAsync(new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Debug }, 1,
            "test-operator", "Enable debugging", cancellationToken: TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (!logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            await Task.Delay(25, deadline.Token);
        logger.LogDebug("Runtime soak marker");
        Assert.Contains(sink.Events, value => value.Level == LogEventLevel.Debug
            && value.MessageTemplate.Text == "Runtime soak marker");
        Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Debug, forwarded.Levels);
        Assert.False(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.Test")
            .IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug));
        Assert.Equal(sentinel, await client.GetFromJsonAsync<Guid>("/sentinel", TestContext.Current.CancellationToken));
        var applied = probe.ReadStatus();
        Assert.Equal(initial.InstanceId, applied.InstanceId);
        Assert.Equal("Debug", applied.EffectiveMinimumLevel);
        Assert.True(applied.ProbeDebugEnabled);
        Assert.True(applied.ApplyCount > initial.ApplyCount);
        await store.PublishAsync(new RuntimeLoggingControls { MinimumLevel = RuntimeLoggingLevel.Verbose }, 2,
            "test-operator", "Expired controls restore startup, not revision one",
            expiresAtUtc: TimeProvider.System.GetUtcNow().AddSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
        while (probe.ReadStatus().EffectiveMinimumLevel != "Verbose")
            await Task.Delay(25, deadline.Token);
        while (probe.ReadStatus().EffectiveMinimumLevel != startupMinimumLevel.ToString())
            await Task.Delay(25, deadline.Token);
        var expired = probe.ReadStatus();
        Assert.Equal(initial.InstanceId, expired.InstanceId);
        Assert.Equal(startupMinimumLevel.ToString(), expired.StartupMinimumLevel);
        Assert.Equal(startupMinimumLevel == LogEventLevel.Debug, expired.ProbeDebugEnabled);
        Assert.True(expired.ApplyCount > applied.ApplyCount);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class CaptureProvider : ILoggerProvider, Microsoft.Extensions.Logging.ILogger
    {
        public const string Category = "RuntimeSoak.Application";
        public ConcurrentQueue<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            categoryName == Category ? this : NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Levels.Enqueue(logLevel);

        public void Dispose() { }
    }
}
#endif
