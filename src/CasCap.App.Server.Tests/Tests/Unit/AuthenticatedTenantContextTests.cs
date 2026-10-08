using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CasCap.Tests.Unit;

/// <summary>Tests claim-derived tenant identity and persistent key confidentiality.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class AuthenticatedTenantContextTests
{
    [Fact]
    public void TenantId_AuthenticatedCallerMappingIsReturned()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("azp", "caller-a")],
                authenticationType: "Test")),
        };
        var context = new AuthenticatedTenantContext(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(new TenantAuthenticationConfig
            {
                Enabled = true,
                TenantCallers = new Dictionary<string, string[]>
                {
                    ["tenant-a"] = ["caller-a", "tenant-a-admin"],
                },
            }));

        Assert.Equal("tenant-a", context.TenantId);
    }

    [Fact]
    public void TenantId_MissingClaimIsRejected()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test")),
        };
        var context = new AuthenticatedTenantContext(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(new TenantAuthenticationConfig
            {
                Enabled = true,
                TenantCallers = new Dictionary<string, string[]> { ["tenant-a"] = ["caller-a"] },
            }));

        Assert.Throws<UnauthorizedAccessException>(() => context.TenantId);
    }

    [Fact]
    public void TenantId_UnmappedCallerIsRejected()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("azp", "unknown-caller")],
                authenticationType: "Test")),
        };
        var context = new AuthenticatedTenantContext(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(new TenantAuthenticationConfig
            {
                Enabled = true,
                TenantCallers = new Dictionary<string, string[]> { ["tenant-a"] = ["caller-a"] },
            }));

        Assert.Throws<UnauthorizedAccessException>(() => context.TenantId);
    }

    [Fact]
    public void CreateRedis_DoesNotDiscloseIdentifiers()
    {
        const string definitionVersion = "definition-version-one";
        var key = AgentStateKey.CreateRedis("session", "tenant-a", "assistant", definitionVersion, "conversation-42");

        Assert.StartsWith("agentizr:v1:session:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-a", key, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant", key, StringComparison.Ordinal);
        Assert.DoesNotContain(definitionVersion, key, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation-42", key, StringComparison.Ordinal);
    }
}
