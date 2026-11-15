namespace CasCap.IntegrationTests;

/// <summary>Exercises disabled and invalid knowledge requests through the real controller without inference.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class DocumentKnowledgeProtocolTests(AgentizrWebApplicationFactory factory)
    : IClassFixture<AgentizrWebApplicationFactory>
{
    [Fact]
    public async Task DisabledKnowledge_Returns404AndDoesNotAdvertiseCacheableContent()
    {
        using var http = factory.CreateClient();
        using var search = await http.PostAsJsonAsync("/api/v1/agents/assistant/knowledge/search",
            new SearchKnowledgeRequest { Query = "synthetic short query" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, search.StatusCode);
        Assert.True(search.Headers.CacheControl?.NoStore);
        using var preview = await http.GetAsync("/api/v1/agents/assistant/knowledge/sources/example-index/chunks/0",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.True(preview.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"query":" "}""")]
    [InlineData("""{"query":"synthetic","maximumResults":0}""")]
    [InlineData("""{"query":"synthetic","maximumResults":9}""")]
    [InlineData("""{"query":"synthetic","maximumCefrLevel":"A3"}""")]
    public async Task InvalidKnowledgeRequest_ReturnsValidationProblemWithoutInference(string body)
    {
        using var http = factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/api/v1/agents/assistant/knowledge/search", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.NotEmpty(problem!.Errors);
    }
}
