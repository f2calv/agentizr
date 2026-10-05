using CasCap.AgentRuntime.Client.Abstractions;
using CasCap.AgentRuntime.Client.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the typed Agent Runtime client.</summary>
public static class AgentRuntimeClientServiceCollectionExtensions
{
    /// <summary>Registers validated options and the typed HTTP client.</summary>
    public static IServiceCollection AddAgentRuntimeClient(this IServiceCollection services)
    {
        services.AddOptions<AgentRuntimeClientOptions>()
            .BindConfiguration(AgentRuntimeClientOptions.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IAgentRuntimeClient, CasCap.AgentRuntime.Client.AgentRuntimeClient>((serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = serviceProvider
                .GetRequiredService<IOptions<AgentRuntimeClientOptions>>()
                .Value
                .BaseAddress;
        });
        return services;
    }
}
