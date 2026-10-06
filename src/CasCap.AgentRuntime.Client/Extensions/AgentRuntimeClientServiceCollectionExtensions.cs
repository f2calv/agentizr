using CasCap.AgentRuntime.Client.Abstractions;
using CasCap.AgentRuntime.Client.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the typed Agent Runtime client.</summary>
public static class AgentRuntimeClientServiceCollectionExtensions
{
    /// <summary>Registers validated options and the typed HTTP client.</summary>
    /// <returns>The HTTP client builder for caller-owned authentication and resilience handlers.</returns>
    public static IHttpClientBuilder AddAgentRuntimeClient(this IServiceCollection services)
    {
        services.AddOptions<AgentRuntimeClientOptions>()
            .BindConfiguration(AgentRuntimeClientOptions.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddHttpClient<IAgentRuntimeClient, CasCap.AgentRuntime.Client.AgentRuntimeClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<AgentRuntimeClientOptions>>()
                .Value;
            httpClient.BaseAddress = options.BaseAddress;
            httpClient.Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes);
        });
    }
}
