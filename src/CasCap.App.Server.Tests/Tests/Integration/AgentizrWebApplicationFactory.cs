using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace CasCap.IntegrationTests;

/// <summary>Runs agentizr with a synthetic tenant definition and deterministic executor.</summary>
public sealed class AgentizrWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CasCap:AgentRuntimeConfig:DefaultTenantId"] = "default",
                ["CasCap:AgentRuntimeConfig:Tenants:default:DefinitionVersion"] = "test-v1",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:test:Type"] = nameof(AgentType.Ollama),
                ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:test:ModelName"] = "test-model",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:test:Endpoint"] = "http://localhost:11434",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:assistant:Provider"] = "test",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:assistant:Name"] = "assistant",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:assistant:Description"] = "Test assistant",
                ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:assistant:Prompt"] = "Test prompt",
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentExecutor>();
            services.AddScoped<IAgentExecutor, StubAgentExecutor>();
        });
    }

    private sealed class StubAgentExecutor : IAgentExecutor
    {
        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentExecutionResult
            {
                OutputText = $"echo:{context.Request.Input}",
                SessionStateJson = context.SessionStateJson ?? "{}",
            });
    }
}
