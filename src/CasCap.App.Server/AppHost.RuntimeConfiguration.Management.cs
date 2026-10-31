using CasCap.Common.Configuration.Management.Contracts.Constants;
using CasCap.Common.Configuration.Management.Extensions;
using CasCap.Constants;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Authorization;

namespace CasCap;

/// <summary>Opts the Debug host into explicitly authorized, host-wide logging management.</summary>
public static partial class AppHost
{
    internal static void AddRuntimeLoggingManagement(WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("CasCap:RuntimeConfigurationConfig")
            .Get<RuntimeConfigurationConfig>() ?? new();
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
            manager.FeatureProviders.Add(new RuntimeLoggingControllerFeatureProvider(settings.Enabled && settings.ManagementEnabled)));
        if (!settings.ManagementEnabled)
            return;
        if (!settings.Enabled
            || !builder.Configuration.GetValue<bool>($"{AdministrationConfig.ConfigurationSectionName}:Enabled"))
            throw new InvalidOperationException("Runtime logging management requires the runtime reader and administration cookie sign-in.");
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        builder.Services.AddSingleton<RuntimeLoggingManagementService>();
        builder.Services.AddRuntimeConfigurationManagement<RuntimeLoggingControls>();
        builder.Services.AddOptions<AuthorizationOptions>().Configure<RuntimeLoggingManagementService>((options, managementSvc) =>
        {
            foreach (var name in new[]
            {
                RuntimeConfigurationManagementPolicies.View,
                RuntimeConfigurationManagementPolicies.Edit,
                RuntimeConfigurationManagementPolicies.Publish,
            })
                options.AddPolicy(name, policy =>
                {
                    policy.AddAuthenticationSchemes(AdministrationAuthenticationSchemes.Cookie);
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(context => managementSvc.IsOperator(context.User));
                });
        });
    }
}
