using ModelContextProtocol.Client;
using System.Text;

namespace CasCap.IntegrationTests;

/// <summary>Exercises the opt-in MCP endpoint over real Streamable HTTP.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AgentizrMcpEndpointTests(McpAgentizrWebApplicationFactory factory)
    : IClassFixture<McpAgentizrWebApplicationFactory>
{
    private const string McpPath = "/mcp";

    [Fact]
    public async Task ListTools_AdvertisesOnlyTheReadOnlyStatusTool()
    {
        await using var mcpClient = await CreateMcpClientAsync();

        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools);
        Assert.Equal("get_agentizr_status", tool.Name);
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
    }

    [Fact]
    public async Task GetAgentizrStatus_ReturnsIdentityAndTime()
    {
        await using var mcpClient = await CreateMcpClientAsync();

        var result = await mcpClient.CallToolAsync("get_agentizr_status", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<System.Text.Json.JsonElement>(result.StructuredContent);
        Assert.Equal("agentizr", structured.GetProperty("service").GetString());
        Assert.True(structured.TryGetProperty("utcNow", out _));
    }

    [Fact]
    public async Task RequestWithOrigin_IsForbiddenBeforeDispatch()
    {
        using var client = factory.CreateClient();
        using var request = CreateInitializeRequest();
        request.Headers.Add("Origin", "https://browser.example.com");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    internal static HttpRequestMessage CreateInitializeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpPath)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private async Task<McpClient> CreateMcpClientAsync()
    {
        var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, McpPath),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, httpClient, loggerFactory: null, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }
}
