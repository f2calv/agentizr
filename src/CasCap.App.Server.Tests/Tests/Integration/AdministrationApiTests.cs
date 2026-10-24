using CasCap.Client.Constants;
using CasCap.Client.Models;

namespace CasCap.IntegrationTests;

/// <summary>Exercises the browser administration API in open Development mode.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AdministrationApiTests(AgentDefinitionAdministrationWebApplicationFactory factory)
    : IClassFixture<AgentDefinitionAdministrationWebApplicationFactory>
{
    private const string AppId = "default";

    [Fact]
    public async Task Discovery_ListsAppsAgentsAndProviders()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var apps = await client.GetFromJsonAsync<AppSummaryResponse[]>(AdministrationRoutes.Apps, cancellationToken);
        var agents = await client.GetFromJsonAsync<AgentSummaryPageResponse>(
            AdministrationRoutes.Format(AdministrationRoutes.Agents, AppId) + "?search=assist&pageSize=10",
            cancellationToken);
        var catalogue = await client.GetFromJsonAsync<ProviderCatalogueResponse>(
            AdministrationRoutes.Format(AdministrationRoutes.Providers, AppId),
            cancellationToken);

        var app = Assert.Single(apps!);
        Assert.Equal(AppId, app.AppId);
        Assert.True(app.AgentCount > 0);
        var assistant = Assert.Single(agents!.Items, item => item.AgentName == "assistant");
        Assert.Equal("test-v1", assistant.ActiveVersion);
        Assert.Equal(nameof(AgentType.Ollama), assistant.ActiveProviderType);
        Assert.Equal(10, agents.PageSize);
        var provider = Assert.Single(catalogue!.Providers);
        Assert.Equal("test", provider.Name);
        Assert.Equal("http://localhost:11434", provider.EndpointAuthority);
        Assert.Equal(ProviderCredentialSources.None, provider.CredentialSource);
        Assert.True(provider.IsEndpointAllowed);
        Assert.Contains("https://mcp.example.com", catalogue.AllowedMcpAuthorities);
    }

    [Fact]
    public async Task GuardedActivation_RejectsStaleDecisions()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        const string agentName = "guarded";

        using var published = await client.PostAsJsonAsync(
            AdministrationRoutes.Format(AdministrationRoutes.Definitions, AppId, agentName),
            AgentDefinitionTestData.CreatePublishRequest("v1"),
            cancellationToken);
        using var activated = await PutActivationAsync(client, agentName, "v1", expectedActiveVersion: null);
        using var staleActivation = await PutActivationAsync(client, agentName, "v1", expectedActiveVersion: null);
        using var staleDeactivation = await PostDeactivationAsync(client, agentName, expectedActiveVersion: "v0");
        using var deactivated = await PostDeactivationAsync(client, agentName, expectedActiveVersion: "v1");
        var activations = await client.GetFromJsonAsync<AgentDefinitionActivationResponse[]>(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionActivations, AppId, agentName),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionVersion, AppId, agentName, "v1"),
            published.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleActivation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleDeactivation.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        Assert.Equal([true, false], activations!.Select(activation => activation.IsDeactivation));
        Assert.All(activations!, activation => Assert.Equal("development", activation.ActivatedBy));
    }

    [Fact]
    public async Task UnknownApp_IsRejectedWithoutRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            AdministrationRoutes.Format(AdministrationRoutes.Agents, "unknown-app"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static Task<HttpResponseMessage> PutActivationAsync(
        HttpClient client,
        string agentName,
        string definitionVersion,
        string? expectedActiveVersion) =>
        client.PutAsJsonAsync(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionActivation, AppId, agentName, definitionVersion),
            new AdministrationActivationRequest { ChangeReason = "activate", ExpectedActiveVersion = expectedActiveVersion },
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostDeactivationAsync(
        HttpClient client,
        string agentName,
        string expectedActiveVersion) =>
        client.PostAsJsonAsync(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionDeactivation, AppId, agentName),
            new AdministrationDeactivationRequest { ChangeReason = "retire", ExpectedActiveVersion = expectedActiveVersion },
            TestContext.Current.CancellationToken);
}
