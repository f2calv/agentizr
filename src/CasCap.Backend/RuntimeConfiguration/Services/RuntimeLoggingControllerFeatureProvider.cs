using CasCap.RuntimeConfiguration.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace CasCap.RuntimeConfiguration.Services;

/// <summary>Removes the optional bootstrap controller when the logging editor is disabled.</summary>
internal sealed class RuntimeLoggingControllerFeatureProvider(bool enabled) : IApplicationFeatureProvider<ControllerFeature>
{
    /// <inheritdoc/>
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (!enabled)
            feature.Controllers.Remove(typeof(RuntimeLoggingManagementController).GetTypeInfo());
    }
}
