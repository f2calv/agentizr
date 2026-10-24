using CasCap.Client.Constants;
using CasCap.Client.Models;

namespace CasCap.IntegrationTests;

/// <summary>Exercises administration with sign-in enabled: per-app assignment, session description and audit identity.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AdministrationSignInTests(AdministrationSignInWebApplicationFactory factory)
    : IClassFixture<AdministrationSignInWebApplicationFactory>
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("00000000-0000-0000-0000-0000000000ff", HttpStatusCode.Forbidden)]
    [InlineData(AdministrationSignInWebApplicationFactory.OtherAppAdministrator, HttpStatusCode.Forbidden)]
    [InlineData(AdministrationSignInWebApplicationFactory.DefaultAppAdministrator, HttpStatusCode.OK)]
    public async Task AppAccess_RequiresAnAssignmentToTheRouteApp(string? administratorId, HttpStatusCode expected)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (administratorId is not null)
            client.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader, administratorId);

        using var response = await client.GetAsync(
            AdministrationRoutes.Format(AdministrationRoutes.Agents, "default"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Apps_ListOnlyAssignedApps()
    {
        using var client = factory.CreateClient();
        await AdministrationTestClient.PrepareAsync(client, AdministrationSignInWebApplicationFactory.OtherAppAdministrator);

        var apps = await client.GetFromJsonAsync<AppSummaryResponse[]>(
            AdministrationRoutes.Apps,
            TestContext.Current.CancellationToken);

        Assert.Equal(["other"], apps!.Select(app => app.AppId));
    }

    [Fact]
    public async Task Publication_IsAuditedAsTheSignedInAdministrator()
    {
        using var client = factory.CreateClient();
        var session = await AdministrationTestClient.PrepareAsync(
            client,
            AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);

        using var response = await client.PostAsJsonAsync(
            AdministrationRoutes.Format(AdministrationRoutes.Definitions, "default", "audited"),
            AgentDefinitionTestData.CreatePublishRequest("v1"),
            TestContext.Current.CancellationToken);
        var snapshot = await response.Content.ReadFromJsonAsync<AgentDefinitionSnapshotResponse>(
            TestContext.Current.CancellationToken);

        Assert.True(session.SignInRequired);
        Assert.True(session.IsAuthenticated);
        Assert.Equal("Example Administrator", session.DisplayName);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"subject-{AdministrationSignInWebApplicationFactory.DefaultAppAdministrator}", snapshot!.PublishedBy);
    }

    [Fact]
    public async Task Logout_RequiresAntiforgeryToken()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            AdministrationSignInWebApplicationFactory.AdministratorHeader,
            AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);

        using var response = await client.PostAsync(AdministrationRoutes.Logout, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/apps/default", "/apps/default")]
    [InlineData("https://example.com/", "/")]
    [InlineData("//example.com/", "/")]
    [InlineData("/\\example.com/", "/")]
    [InlineData(null, "/")]
    public void GetLocalReturnUrl_RejectsOffSiteTargets(string? returnUrl, string expected) =>
        Assert.Equal(expected, AdministrationSessionService.GetLocalReturnUrl(returnUrl));
}
