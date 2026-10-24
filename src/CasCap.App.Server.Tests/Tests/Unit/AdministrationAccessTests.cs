using CasCap.Client.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace CasCap.Tests.Unit;

/// <summary>Tests configuration-only administrator assignment and route-selected administration tenancy.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class AdministrationAccessTests
{
    private const string AdministratorId = "00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData("app-a", AdministratorId, true)]
    [InlineData("app-b", AdministratorId, false)]
    [InlineData("unknown-app", AdministratorId, false)]
    [InlineData("app-a", "00000000-0000-0000-0000-000000000002", false)]
    [InlineData("app-a", null, false)]
    public void CanAdminister_RequiresAnAssignmentForAKnownApp(string appId, string? administratorId, bool expected)
    {
        var accessSvc = CreateAccessService(administrationEnabled: true, tenantAuthenticationEnabled: true, Environments.Production);

        Assert.Equal(expected, accessSvc.CanAdminister(CreateAdministrator(administratorId), appId));
    }

    [Fact]
    public void GetAdministeredApps_ListsOnlyAssignedApps()
    {
        var accessSvc = CreateAccessService(administrationEnabled: true, tenantAuthenticationEnabled: true, Environments.Production);

        Assert.Equal(["app-a"], accessSvc.GetAdministeredApps(CreateAdministrator(AdministratorId)));
        Assert.Empty(accessSvc.GetAdministeredApps(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Theory]
    [InlineData(false, false, "Development", true)]
    [InlineData(true, false, "Development", false)]
    [InlineData(false, true, "Development", false)]
    [InlineData(false, false, "Production", false)]
    public void IsOpenDevelopmentAccess_RequiresDevelopmentWithoutAuthentication(
        bool administrationEnabled,
        bool tenantAuthenticationEnabled,
        string environmentName,
        bool expected)
    {
        var accessSvc = CreateAccessService(administrationEnabled, tenantAuthenticationEnabled, environmentName);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(expected, accessSvc.IsOpenDevelopmentAccess);
        Assert.Equal(expected, accessSvc.CanAdminister(anonymous, "app-b"));
    }

    [Fact]
    public void AdministrationConfig_EnabledRequiresAssignments()
    {
        var config = new AdministrationConfig { Enabled = true };

        Assert.NotEmpty(config.Validate(new ValidationContext(config)));
        Assert.Empty(new AdministrationConfig().Validate(new ValidationContext(config)));
    }

    [Theory]
    [InlineData(true, "app-a")]
    [InlineData(false, "caller-tenant")]
    public void TenantId_UsesTheRouteAppOnlyOnAdministrationEndpoints(bool isAdministrationEndpoint, string expected)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues[AdministrationRoutes.AppIdParameter] = "app-a";
        var metadata = isAdministrationEndpoint
            ? new EndpointMetadataCollection(new AuthorizeAttribute(AdministrationPolicies.AppAdministrator))
            : new EndpointMetadataCollection(new AuthorizeAttribute(AgentRuntimePolicies.DefinitionRead));
        httpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        var tenantContext = new AdministrationTenantContext(
            new HttpContextAccessor { HttpContext = httpContext },
            new StaticTenantContext("caller-tenant"));

        Assert.Equal(expected, tenantContext.TenantId);
    }

    private static AdministrationAccessService CreateAccessService(
        bool administrationEnabled,
        bool tenantAuthenticationEnabled,
        string environmentName) =>
        new(
            Options.Create(new AdministrationConfig
            {
                Enabled = administrationEnabled,
                AppAdministrators = new Dictionary<string, string[]> { ["app-a"] = [AdministratorId] },
            }),
            Options.Create(new TenantAuthenticationConfig
            {
                Enabled = tenantAuthenticationEnabled,
                TenantCallers = new Dictionary<string, string[]> { ["app-b"] = ["caller-b"] },
            }),
            Options.Create(new AgentRuntimeConfig()),
            new TestHostEnvironment(environmentName));

    private static ClaimsPrincipal CreateAdministrator(string? administratorId) =>
        new(new ClaimsIdentity(
            administratorId is null ? [] : [new Claim("oid", administratorId)],
            authenticationType: "Test"));

    private sealed class StaticTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "agentizr";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
