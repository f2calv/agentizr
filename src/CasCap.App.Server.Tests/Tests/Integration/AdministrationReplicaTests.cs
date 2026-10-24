using CasCap.Client.Constants;
using CasCap.IntegrationTests;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace CasCap.Tests.Integration;

/// <summary>Verifies that administration tokens survive a request moving to another replica through shared PostgreSQL keys.</summary>
[Trait("Category", "PostgreSQL")]
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AdministrationReplicaTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task AntiforgeryToken_IssuedByOneReplicaIsAcceptedByAnother()
    {
        var cookies = new CookieContainer();
        using var firstReplica = new PostgresReplicaWebApplicationFactory(fixture.ConnectionString);
        using var secondReplica = new PostgresReplicaWebApplicationFactory(fixture.ConnectionString);
        using var firstClient = firstReplica.CreateDefaultClient(new CookieContainerHandler(cookies));
        using var secondClient = secondReplica.CreateDefaultClient(new CookieContainerHandler(cookies));
        var session = await AdministrationTestClient.PrepareAsync(firstClient);
        secondClient.DefaultRequestHeaders.Add(AdministrationRoutes.AntiforgeryHeaderName, session.AntiforgeryToken);

        using var published = await secondClient.PostAsJsonAsync(
            AdministrationRoutes.Format(AdministrationRoutes.Definitions, "default", "replicated"),
            AgentDefinitionTestData.CreatePublishRequest("v1"),
            TestContext.Current.CancellationToken);

        await using var dbContext = await fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.NotEmpty(await dbContext.DataProtectionKeys.ToListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class PostgresReplicaWebApplicationFactory(string connectionString) : AgentizrWebApplicationFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            const string key = "CasCap__DatabaseConfig__ConnectionString";
            var previous = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, connectionString);
            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, previous);
            }
        }
    }
}
