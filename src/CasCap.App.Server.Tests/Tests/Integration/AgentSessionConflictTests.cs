using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Checks that every session mutation refuses overlap, including before streaming starts.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AgentSessionConflictTests(AgentizrWebApplicationFactory factory)
    : IClassFixture<AgentizrWebApplicationFactory>
{
    [Theory]
    [InlineData("POST", "runs")]
    [InlineData("POST", "runs/stream")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "compact")]
    [InlineData("PUT", "snapshots/saved")]
    [InlineData("POST", "snapshots/saved/activate")]
    [InlineData("DELETE", "snapshots/saved")]
    [InlineData("PUT", "overrides")]
    public async Task Mutation_BusySessionReturnsConflict(string method, string operation)
    {
        using var scope = factory.Services.CreateScope();
        var sessionId = Guid.NewGuid().ToString("N");
        await using var lease = await scope.ServiceProvider.GetRequiredService<IAgentSessionLock>()
            .AcquireAsync("assistant", sessionId, TestContext.Current.CancellationToken);
        var path = operation.StartsWith("runs", StringComparison.Ordinal)
            ? $"/api/v1/agents/assistant/{operation}"
            : $"/api/v1/agents/assistant/sessions/{sessionId}"
                + (operation.Length == 0 ? string.Empty : $"/{operation}");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (operation.StartsWith("runs", StringComparison.Ordinal))
            request.Content = JsonContent.Create(new RunAgentRequest { SessionId = sessionId, Input = "hello" });
        else if (operation == "compact")
            request.Content = JsonContent.Create(new CompactAgentSessionRequest { RetainMessageCount = 1 });
        else if (operation == "overrides")
            request.Content = JsonContent.Create(new UpdateAgentOverridesRequest { SessionEnabled = true });
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
    }
}
