using CasCap.Common.Extensions;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Executes resolved definitions through the shared Common.AI agent primitives.</summary>
internal sealed class CommonAgentExecutor(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment,
    IAgentDefinitionStore definitionStore,
    IProviderCredentialStore credentialStore) : IAgentExecutor
{
    /// <inheritdoc/>
    public async ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        var executionStopwatch = Stopwatch.StartNew();
        var events = new ConcurrentQueue<AgentExecutionEvent>();
        var runScope = new AgentRunScope
        {
            OnCompaction = stats => PublishEvent(context.Request, events, new AgentExecutionEvent
            {
                Type = RunAgentEventTypes.SessionCompacted,
                Elapsed = executionStopwatch.Elapsed,
                InputMessageCount = stats.InputCount,
                OutputMessageCount = stats.OutputCount,
                ToolMessagesDropped = stats.ToolDropped,
                WindowMessagesTrimmed = stats.WindowTrimmed,
                TargetMessageCount = stats.Target,
            }),
        };

        var result = await ExecuteDefinitionAsync(
            context.Definition,
            context.ProviderApiKey,
            context.Request,
            context.Overrides,
            context.SessionStateJson,
            runScope,
            events,
            executionStopwatch,
            cancellationToken);

        return result with { Events = [.. events] };
    }

    private async ValueTask<AgentExecutionResult> ExecuteDefinitionAsync(
        AgentDefinition definition,
        string? providerApiKey,
        AgentExecutionRequest request,
        AgentOverrideState overrides,
        string? sessionStateJson,
        AgentRunScope runScope,
        ConcurrentQueue<AgentExecutionEvent> events,
        Stopwatch executionStopwatch,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger<CommonAgentExecutor>();
        var toolLeases = new List<IAsyncDisposable>();
        var tools = await BuildToolsAsync(
            definition,
            request,
            runScope,
            events,
            executionStopwatch,
            toolLeases,
            cancellationToken);

        var provider = definition.Provider with { ApiKey = providerApiKey };
        var httpClient = httpClientFactory.CreateClient(nameof(CommonAgentExecutor));
        httpClient.BaseAddress = provider.Endpoint;
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var (_, agent, instructions) = AgentExtensions.CreateAgent(
            provider,
            definition.Agent,
            httpClient,
            tools,
            loggerFactory: loggerFactory);
        var chatOptions = AgentExtensions.BuildChatOptions(definition.Agent, instructions);
        if (!string.IsNullOrWhiteSpace(overrides.ModelName))
            chatOptions.ModelId = overrides.ModelName;
        if (!string.IsNullOrWhiteSpace(overrides.Instructions))
            chatOptions.Instructions = overrides.Instructions;

        AgentSession? session = null;
        if (!string.IsNullOrWhiteSpace(sessionStateJson))
        {
            using var sessionDocument = JsonDocument.Parse(sessionStateJson);
            session = await agent.DeserializeSessionAsync(
                sessionDocument.RootElement.Clone(),
                JsonSerializerOptions.Web).AsTask();
        }

        try
        {
            var result = await agent.RunAnalysisAsync(
                provider,
                definition.Agent,
                AgentExtensions.BuildChatMessage(request.Input, request.BinaryContent, request.MimeType),
                chatOptions,
                session,
                cancellationToken: cancellationToken,
                logger: logger,
                scope: runScope);

            string? updatedSessionStateJson = null;
            if (result.Session is not null)
            {
                var serialized = await agent.SerializeSessionAsync(result.Session, JsonSerializerOptions.Web).AsTask();
                updatedSessionStateJson = serialized.GetRawText();
            }

            return new AgentExecutionResult
            {
                OutputText = result.OutputText,
                SessionStateJson = updatedSessionStateJson,
                DefinitionVersion = definition.Version,
                ModelName = definition.Provider.ModelName,
                Diagnostics = result,
            };
        }
        finally
        {
            foreach (var lease in toolLeases)
                await lease.DisposeAsync();
        }
    }

    private async ValueTask<List<AITool>> BuildToolsAsync(
        AgentDefinition definition,
        AgentExecutionRequest request,
        AgentRunScope runScope,
        ConcurrentQueue<AgentExecutionEvent> events,
        Stopwatch executionStopwatch,
        List<IAsyncDisposable> toolLeases,
        CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        var logger = loggerFactory.CreateLogger<CommonAgentExecutor>();

        foreach (var source in definition.Agent.Tools)
        {
            if (source.Service is not null)
            {
                var serviceAgent = definition.Agent with { Tools = [source] };
                tools.AddRange(AgentExtensions.CreateToolsForAgent(
                    serviceProvider,
                    serviceAgent,
                    deferResolution: true,
                    isDevelopment: hostEnvironment.IsDevelopment(),
                    logger: logger));
                continue;
            }

            if (source.Endpoint is not null)
            {
                var (client, remoteTools) = await AgentExtensions.GetHttpTools(source.Endpoint, logger);
                toolLeases.Add(client);
                tools.AddRange(AgentExtensions.FilterTools(
                    remoteTools,
                    source,
                    hostEnvironment.IsDevelopment(),
                    logger));
                continue;
            }

            if (source.Agent is not null)
                tools.Add(await CreateSubAgentToolAsync(
                    source,
                    request,
                    runScope,
                    events,
                    executionStopwatch,
                    cancellationToken));
        }

        return tools;
    }

    private async ValueTask<AITool> CreateSubAgentToolAsync(
        ToolSource source,
        AgentExecutionRequest request,
        AgentRunScope parentScope,
        ConcurrentQueue<AgentExecutionEvent> events,
        Stopwatch executionStopwatch,
        CancellationToken cancellationToken)
    {
        var agentName = source.Agent!;
        var definition = await definitionStore.GetAsync(agentName, cancellationToken)
            ?? throw new InvalidOperationException($"Delegated agent '{agentName}' is not available to the current tenant.");

        async Task<string> InvokeAgentAsync(
            [Description("The task or question to pass to this specialist agent.")] string task,
            CancellationToken invocationCancellationToken = default)
        {
            var childScope = parentScope.ForSubAgent();
            PublishEvent(request, events, new AgentExecutionEvent
            {
                Type = RunAgentEventTypes.DelegationStarted,
                AgentName = agentName,
                Depth = childScope.Depth,
                ModelName = definition.Provider.ModelName,
                Elapsed = executionStopwatch.Elapsed,
            });

            var apiKey = await credentialStore.GetApiKeyAsync(definition.Agent.Provider, invocationCancellationToken);
            var result = await ExecuteDefinitionAsync(
                definition,
                apiKey,
                new AgentExecutionRequest
                {
                    AgentName = agentName,
                    SessionId = $"delegation-{Guid.NewGuid():N}",
                    Input = task,
                    BypassSession = true,
                },
                new AgentOverrideState { SessionEnabled = false },
                null,
                childScope,
                events,
                executionStopwatch,
                invocationCancellationToken);

            PublishEvent(request, events, new AgentExecutionEvent
            {
                Type = RunAgentEventTypes.DelegationCompleted,
                AgentName = agentName,
                Depth = childScope.Depth,
                ModelName = result.ModelName,
                Elapsed = executionStopwatch.Elapsed,
                Diagnostics = result.Diagnostics,
            });
            return result.OutputText;
        }

        var tool = AIFunctionFactory.Create(InvokeAgentAsync, new AIFunctionFactoryOptions
        {
            Name = $"invoke_{agentName.ToSnakeCase()}",
            Description = definition.Agent.Description,
        });
        return AgentExtensions.FilterTools(
            [tool],
            source,
            hostEnvironment.IsDevelopment(),
            loggerFactory.CreateLogger<CommonAgentExecutor>()).Single();
    }

    private static void PublishEvent(
        AgentExecutionRequest request,
        ConcurrentQueue<AgentExecutionEvent> events,
        AgentExecutionEvent executionEvent)
    {
        events.Enqueue(executionEvent);
        request.EventSink?.Invoke(executionEvent);
    }
}
