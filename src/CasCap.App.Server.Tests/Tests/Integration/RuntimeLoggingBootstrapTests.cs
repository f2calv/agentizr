#if DEBUG
using CasCap.Common.Configuration.Runtime.Models;
using CasCap.IntegrationTests;
using CasCap.Models;
using CasCap.RuntimeConfiguration.Models;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Validates bootstrap selection without starting readers or connecting to a database.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RuntimeLoggingBootstrapTests
{
    [Theory]
    [InlineData(true, "application_database")]
    [InlineData(false, "independent_database")]
    public async Task ConnectionSelection(bool reuse, string expectedDatabase)
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CasCap:RuntimeConfigurationConfig:ApplicationDatabaseReuseEnabled"] = reuse.ToString(),
            ["CasCap:RuntimeConfigurationConfig:ConnectionString"] = reuse
                ? null : "Host=localhost;Port=1;Database=independent_database;Username=example",
        });
        Assert.NotNull(AppHost.AddRuntimeLogging(builder));
        await using var app = builder.Build();
        var options = app.Services.GetRequiredService<RuntimeConfigurationOptions>();
        Assert.Equal("example-application", options.Scope.Application);
        Assert.Equal("Production", options.Scope.Environment);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), options.RefreshInterval);
        await using var runtime = await app.Services.GetRequiredService<IDbContextFactory<CasCap.Common.Configuration.PostgreSql.RuntimeConfigurationDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var runtimeConnection = new NpgsqlConnectionStringBuilder(runtime.Database.GetConnectionString());
        var applicationConfig = builder.Configuration.GetSection(DatabaseConfig.ConfigurationSectionName).Get<DatabaseConfig>();
        Assert.NotNull(applicationConfig);
        var applicationConnection = new NpgsqlConnectionStringBuilder(AppHost.EffectiveDatabaseConnectionString(applicationConfig));
        Assert.Equal(expectedDatabase, runtimeConnection.Database);
        Assert.Equal("application_database", applicationConnection.Database);
        Assert.Equal(GssEncryptionMode.Disable, runtimeConnection.GssEncryptionMode);
        Assert.Equal(GssEncryptionMode.Disable, applicationConnection.GssEncryptionMode);
        Assert.Equal(applicationConnection.Username, runtimeConnection.Username);
        var status = app.Services.GetRequiredService<RuntimeLoggingProbe>().ReadStatus();
        Assert.Equal(0, status.ApplyCount);
        Assert.Null(status.LastAppliedAtUtc);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "Host=localhost;Port=1;Database=example")]
    public void AmbiguousOrMissingConnection(bool reuse, string? connection)
    {
        var builder = CreateBuilder();
        using var configuration = builder.Configuration;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CasCap:RuntimeConfigurationConfig:ApplicationDatabaseReuseEnabled"] = reuse.ToString(),
            ["CasCap:RuntimeConfigurationConfig:ConnectionString"] = connection,
        });
        Assert.Throws<ValidationException>(() => AppHost.AddRuntimeLogging(builder));
    }

    [Fact]
    public void MissingApplicationConnection()
    {
        var builder = CreateBuilder();
        using var configuration = builder.Configuration;
        builder.Configuration["CasCap:RuntimeConfigurationConfig:ApplicationDatabaseReuseEnabled"] = "true";
        builder.Configuration["CasCap:DatabaseConfig:ConnectionString"] = null;
        Assert.Throws<InvalidOperationException>(() => AppHost.AddRuntimeLogging(builder));
    }

    [Fact]
    public async Task DisabledRequiresNoConnection()
    {
        var builder = CreateBuilder();
        builder.Configuration["CasCap:RuntimeConfigurationConfig:Enabled"] = "false";
        Assert.Null(AppHost.AddRuntimeLogging(builder));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(RuntimeLoggingProbe));
        await using var app = builder.Build();
        AppHost.MapStatusEndpoint(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var status = await client.GetFromJsonAsync<JsonElement>("/status", TestContext.Current.CancellationToken);
        Assert.Single(status.EnumerateObject());
        Assert.Equal("agentizr", status.GetProperty("service").GetString());
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CasCap:RuntimeConfigurationConfig:Enabled"] = "true",
            ["CasCap:RuntimeConfigurationConfig:Application"] = "example-application",
            ["CasCap:DatabaseConfig:ConnectionString"] = "Host=localhost;Port=1;Database=shared_database;Username=example",
            ["CasCap:DatabaseConfig:DatabaseName"] = "application_database",
            ["Serilog:MinimumLevel:Default"] = "Information",
        });
        return builder;
    }
}
#endif
