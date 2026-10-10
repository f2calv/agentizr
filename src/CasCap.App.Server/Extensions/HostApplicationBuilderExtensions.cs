using CasCap.Common.Extensions;
using CasCap.Common.Models;
using CasCap.Models;
using System.Reflection;

namespace CasCap.Extensions;

/// <summary>Provides standard configuration bootstrapping for the Agent Runtime host.</summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>Adds standard configuration sources and binds application configuration.</summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="assembly">Assembly used for user-secrets loading.</param>
    /// <returns>The application configuration and deployment metadata.</returns>
    public static (AppConfig appConfig, GitMetadata gitMetadata) InitializeConfiguration(
        this IHostApplicationBuilder builder,
        Assembly assembly)
    {
        builder.Configuration.AddStandardConfiguration(builder.Environment.EnvironmentName, assembly);

        var azureAuthConfig = builder.Configuration
            .GetSection(AzureAuthConfig.ConfigurationSectionName)
            .Get<AzureAuthConfig>();
        if (azureAuthConfig?.IsKeyVaultEnabled is true)
        {
            builder.Configuration.AddKeyVaultConfiguration(
                azureAuthConfig.KeyVaultUri,
                azureAuthConfig.TokenCredential,
                new PrefixKeyVaultSecretManager(
                    "AgentRuntime--Agentizr",
                    nameof(CasCap),
                    "AgentRuntime"));
        }

        var appConfig = builder.Configuration
            .GetSection(AppConfig.ConfigurationSectionName)
            .Get<AppConfig>() ?? new AppConfig();
        builder.Services.AddOptionsWithValidateOnStart<AppConfig>()
            .BindConfiguration(AppConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var gitMetadata = new GitMetadata();
        builder.Services.AddSingleton(gitMetadata);

        return (appConfig, gitMetadata);
    }
}
