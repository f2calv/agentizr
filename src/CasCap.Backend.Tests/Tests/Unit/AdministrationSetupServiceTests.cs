using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace CasCap.Tests.Unit;

/// <summary>Tests the configuration explanations shown instead of the administration frontend or sign-in.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class AdministrationSetupServiceTests
{
    [Fact]
    public void FrontendUnavailablePage_ReportsSettingStatusWithoutValues()
    {
        const string authority = "https://login.example.com/00000000-0000-0000-0000-000000000000/v2.0";
        const string clientSecret = "configured-secret-value";
        var setupSvc = CreateService(new AdministrationConfig
        {
            Authority = authority,
            ClientSecret = clientSecret,
            AppAdministrators = new Dictionary<string, string[]> { ["app-a"] = ["00000000-0000-0000-0000-000000000001"] },
        });

        var page = setupSvc.RenderFrontendUnavailablePage(CreateRequest(forwardedProto: null));

        Assert.Contains("this host serves only the agent runtime API", page, StringComparison.Ordinal);
        Assert.Contains("CasCap:AdministrationConfig:ClientId</code></td><td>missing", page, StringComparison.Ordinal);
        Assert.Contains("CasCap:AdministrationConfig:ClientSecret</code></td><td>set", page, StringComparison.Ordinal);
        Assert.Contains("set for 1 app(s)", page, StringComparison.Ordinal);
        Assert.DoesNotContain(clientSecret, page, StringComparison.Ordinal);
        Assert.DoesNotContain(authority, page, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000001", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ASPNETCORE_FORWARDEDHEADERS_ENABLED", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https", true)]
    [InlineData("http", false)]
    [InlineData(null, false)]
    public void IsForwardedSchemeIgnored_DetectsUnappliedForwardedHttps(string? forwardedProto, bool expected) =>
        Assert.Equal(expected, AdministrationSetupService.IsForwardedSchemeIgnored(CreateRequest(forwardedProto)));

    [Fact]
    public void FrontendUnavailablePage_EncodesTheRequestHost()
    {
        var request = CreateRequest(forwardedProto: null);
        request.Host = new HostString("<script>");

        var page = CreateService(new AdministrationConfig()).RenderFrontendUnavailablePage(request);

        Assert.DoesNotContain("<script>", page, StringComparison.Ordinal);
    }

    private static AdministrationSetupService CreateService(AdministrationConfig config) =>
        new(
            Options.Create(config),
            Options.Create(new TenantAuthenticationConfig()),
            new HostingEnvironment { EnvironmentName = Environments.Production });

    private static HttpRequest CreateRequest(string? forwardedProto)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("app.example.com");
        if (forwardedProto is not null)
            request.Headers["X-Forwarded-Proto"] = forwardedProto;
        return request;
    }
}
