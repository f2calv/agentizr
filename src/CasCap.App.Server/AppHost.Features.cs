using CasCap.Common.Models;

namespace CasCap;

/// <summary>Registers application-level host services.</summary>
public static partial class AppHost
{
    private static void AddFeatures(
        WebApplicationBuilder builder,
        GitMetadata gitMetadata)
    {
        builder.Services.AddSingleton(gitMetadata);
    }
}
