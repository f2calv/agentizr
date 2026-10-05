using System.Net.Http.Json;

namespace CasCap.IntegrationTests;

/// <summary>Exercises the v1 HTTP contract through the published typed client.</summary>
[Trait("Category", "Integration")]
public sealed class AgentRuntimeProtocolTests(AgentizrWebApplicationFactory factory)
    : IClassFixture<AgentizrWebApplicationFactory>
{
    [Fact]
    public async Task RunAgent_ReturnsVersionedResponse()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentRuntimeClient(httpClient);

        var response = await client.RunAgentAsync(
            "assistant",
            new RunAgentRequest { SessionId = "conversation-42", Input = "hello" },
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("conversation-42", response.SessionId);
        Assert.Equal("echo:hello", response.OutputText);
        Assert.Equal("test-v1", response.DefinitionVersion);
    }

    [Fact]
    public async Task RunAgent_UnknownAgentReturnsNull()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentRuntimeClient(httpClient);

        var response = await client.RunAgentAsync(
            "missing",
            new RunAgentRequest { SessionId = "conversation-42", Input = "hello" },
            CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task RunAgent_InvalidRequestReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/runs",
            new RunAgentRequest { SessionId = string.Empty, Input = string.Empty },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
