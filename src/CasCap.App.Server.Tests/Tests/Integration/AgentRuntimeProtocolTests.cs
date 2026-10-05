namespace CasCap.IntegrationTests;

/// <summary>Exercises the v1 HTTP contract through the published typed client.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
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
            new RunAgentRequest
            {
                SessionId = "conversation-42",
                Input = "hello",
                BinaryContent = [1, 2, 3],
                MimeType = "image/png",
                BypassSession = true,
            },
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("conversation-42", response.SessionId);
        Assert.Equal("echo:hello:3", response.OutputText);
        Assert.Equal("test-v1", response.DefinitionVersion);
        Assert.Equal("test-model", response.ModelName);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(250, response.ElapsedMilliseconds);
        Assert.Equal(50, response.TimeToFirstTokenMilliseconds);
        Assert.Equal(16, response.Usage?.TotalTokenCount);
        Assert.Single(response.ToolCalls, toolCall => toolCall.Name == "get_status");
        Assert.Single(response.Attachments, attachment => attachment.FileName == "status.png");
        Assert.Single(response.Events, executionEvent => executionEvent.Type == "session.compacted");
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

    [Fact]
    public async Task RunAgent_BinaryWithoutMimeTypeReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/runs",
            new RunAgentRequest
            {
                SessionId = "conversation-42",
                Input = "describe this",
                BinaryContent = [1, 2, 3],
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
