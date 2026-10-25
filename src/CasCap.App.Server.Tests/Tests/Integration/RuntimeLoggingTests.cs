#if DEBUG
using CasCap.Common.Configuration.PostgreSql;
using CasCap.Common.Configuration.PostgreSql.Services;
using CasCap.IntegrationTests;
using CasCap.RuntimeConfiguration.Models;
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

namespace CasCap.Tests.Integration;

/// <summary>Verifies the unpublished Debug soak through the real PostgreSQL, monitor and logging pipeline.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RuntimeLoggingTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Reload_ControlsPipelineAndForwardingWithoutReplacingHost()
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
            ["CasCap:RuntimeConfigurationConfig:ConnectionString"] = postgres.ConnectionString,
            ["CasCap:RuntimeConfigurationConfig:RefreshIntervalMs"] = "100",
            ["Serilog:MinimumLevel:Default"] = "Information",
        });
        var levelSwitch = AppHost.AddRuntimeLogging(builder);
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
        await store.PublishAsync(new RuntimeLoggingControls { MinimumLevel = LogEventLevel.Warning }, 0,
            "test-operator", "Initial publication", cancellationToken: TestContext.Current.CancellationToken);
        var cache = app.Services.GetRequiredService<IMemoryCache>();
        var sentinel = Guid.NewGuid();
        cache.Set("sentinel", sentinel);
        app.MapGet("/sentinel", () => cache.Get<Guid>("sentinel"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CaptureProvider.Category);
        Assert.False(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug));
        await store.PublishAsync(new RuntimeLoggingControls { MinimumLevel = LogEventLevel.Debug }, 1,
            "test-operator", "Enable debugging", cancellationToken: TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            await Task.Delay(25, deadline.Token);
        logger.LogDebug("Runtime soak marker");
        Assert.Contains(sink.Events, value => value.Level == LogEventLevel.Debug
            && value.MessageTemplate.Text == "Runtime soak marker");
        Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Debug, forwarded.Levels);
        Assert.False(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.Test")
            .IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug));
        Assert.Equal(sentinel, await client.GetFromJsonAsync<Guid>("/sentinel", TestContext.Current.CancellationToken));
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
