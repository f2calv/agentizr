using CasCap.Constants;
using System.Security.Claims;

namespace CasCap.Tests.Unit;

/// <summary>Tests JWT permission and caller-to-tenant mapping rules.</summary>
[Trait("Category", "Tenant Isolation")]
public sealed class AgentRuntimeAuthorizationTests
{
    [Theory]
    [InlineData("roles")]
    [InlineData("scope")]
    [InlineData("scp")]
    public void HasPermission_ApplicationRoleOrDelegatedScopeIsAccepted(string claimType)
    {
        var principal = CreatePrincipal(new Claim(claimType, $"other {AgentRuntimePermissions.Execute}"));

        var allowed = AgentRuntimeAuthorization.HasPermission(principal, AgentRuntimePermissions.Execute);

        Assert.True(allowed);
    }

    [Fact]
    public void HasPermission_MissingPermissionIsRejected()
    {
        var principal = CreatePrincipal(new Claim("roles", AgentRuntimePermissions.DefinitionRead));

        var allowed = AgentRuntimeAuthorization.HasPermission(principal, AgentRuntimePermissions.Execute);

        Assert.False(allowed);
    }

    [Fact]
    public void HasValidTenantCallers_MultipleCallersPerTenantIsAccepted()
    {
        var mappings = new Dictionary<string, string[]>
        {
            ["smarthaus"] = ["smarthaus-exec", "smarthaus-admin"],
            ["cas"] = ["cas-exec", "cas-admin"],
        };

        Assert.True(AgentRuntimeAuthorization.HasValidTenantCallers(mappings));
    }

    [Fact]
    public void HasValidTenantCallers_DuplicateCallerIsRejected()
    {
        var mappings = new Dictionary<string, string[]>
        {
            ["smarthaus"] = ["shared-caller"],
            ["cas"] = ["SHARED-CALLER"],
        };

        Assert.False(AgentRuntimeAuthorization.HasValidTenantCallers(mappings));
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}