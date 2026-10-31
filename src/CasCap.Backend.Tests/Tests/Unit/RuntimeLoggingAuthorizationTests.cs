#if DEBUG
using CasCap.Common.Configuration.Runtime.Models;
using CasCap.Constants;
using CasCap.RuntimeConfiguration.Models;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;

namespace CasCap.Tests.Unit;

/// <summary>Checks opt-in host-wide authorization independently of database and provider fixtures.</summary>
[Trait("Category", "Authorization")]
public sealed class RuntimeLoggingAuthorizationTests
{
    private const string Operator = "00000000-0000-0000-0000-00000000000a";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppAdministratorGrantMustBeExplicit(bool grant)
    {
        var service = new RuntimeLoggingManagementService(
            Options.Create(new AdministrationConfig
            {
                Enabled = true,
                AppAdministrators = new() { ["sample-app"] = [Operator] },
            }),
            new RuntimeConfigurationConfig
            {
                ManagementEnabled = true,
                ManagementUseAppAdministratorAssignments = grant,
            },
            new RuntimeConfigurationOptions { Scope = new("sample-host", "sample-environment"), ContractId = "logging" });
        Assert.Equal(grant, service.IsOperator(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("oid", Operator)], AdministrationAuthenticationSchemes.Cookie))));
    }

    [Theory]
    [InlineData(false, false, null, true)]
    [InlineData(true, false, null, false)]
    [InlineData(true, false, Operator, true)]
    [InlineData(true, false, " ", false)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, Operator, false)]
    public void RequiresOneExplicitGrant(bool enabled, bool appGrant, string? actor, bool valid)
    {
        var settings = new RuntimeConfigurationConfig
        {
            ApplicationDatabaseReuseEnabled = true,
            ManagementEnabled = enabled,
            ManagementUseAppAdministratorAssignments = appGrant,
            ManagementOperators = actor is null ? [] : [actor],
        };
        Assert.Equal(valid, !settings.Validate(new ValidationContext(settings)).Any());
    }

    [Fact]
    public void DisabledEditorHasNoBootstrapController()
    {
        var feature = new ControllerFeature();
        feature.Controllers.Add(typeof(CasCap.RuntimeConfiguration.Controllers.RuntimeLoggingManagementController).GetTypeInfo());
        new RuntimeLoggingControllerFeatureProvider(enabled: false).PopulateFeature([], feature);
        Assert.Empty(feature.Controllers);
    }
}
#endif
