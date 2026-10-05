namespace CasCap.IntegrationTests;

/// <summary>Credential-free smoke tests for the application host endpoints.</summary>
[Trait("Category", "Integration")]
public sealed class EndpointSmokeTests(AgentizrWebApplicationFactory factory)
    : IClassFixture<AgentizrWebApplicationFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/healthz")]
    public async Task GetEndpoint(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
