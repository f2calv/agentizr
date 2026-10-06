using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
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
            services.RemoveAll<IAgentSessionCodec>();
            services.AddScoped<IAgentSessionCodec, StubAgentSessionCodec>();
        });
    }

    private sealed class StubAgentSessionCodec : IAgentSessionCodec
    {
        public ValueTask<AgentSessionInspection> InspectAsync(
            AgentDefinition definition,
            string? providerApiKey,
            string sessionStateJson,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentSessionInspection
            {
                SizeBytes = sessionStateJson.Length,
            });

        public ValueTask<AgentSessionCompactionResult> CompactAsync(
            AgentDefinition definition,
            string? providerApiKey,
            string sessionStateJson,
            int retainMessageCount,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentSessionCompactionResult
            {
                HistoryAvailable = true,
                RemovedMessageCount = 3,
                SessionStateJson = sessionStateJson,
            });
    }

    private sealed class StubAgentExecutor : IAgentExecutor
    {
        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionContext context,
            CancellationToken cancellationToken)
        {
            var diagnostics = new AgentRunResult(context.Definition.Agent.Name)
            {
                Elapsed = TimeSpan.FromMilliseconds(250),
                FinishReason = "stop",
                ModelName = context.Definition.Provider.ModelName,
                TimeToFirstToken = TimeSpan.FromMilliseconds(50),
                ToolCallCount = 1,
                Usage = new UsageDetails
                {
                    InputTokenCount = 12,
                    OutputTokenCount = 4,
                    TotalTokenCount = 16,
                },
            };
            diagnostics.ToolCalls.Add(new ToolCallInfo("get_status", null));
            diagnostics.Attachments.Add(new AgentRunAttachment
            {
                MimeType = "image/png",
                FileName = "status.png",
                Base64Content = "AQID",
            });

            return ValueTask.FromResult(new AgentExecutionResult
            {
                OutputText = $"echo:{context.Request.Input}:{context.Request.BinaryContent?.Length ?? 0}",
                SessionStateJson = context.SessionStateJson ?? "{}",
                Diagnostics = diagnostics,
                Events =
                [
                    new AgentExecutionEvent
                    {
                        Type = "session.compacted",
                        InputMessageCount = 20,
                        OutputMessageCount = 10,
                    },
                ],
            });
        }
    }
}
