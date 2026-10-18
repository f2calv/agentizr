using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Common.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Client;
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
    IAgentDefinitionStore definitionStore,
    AgentRuntimeBuiltInTools builtInTools,
    IProviderCredentialStore credentialStore,
    IMcpCredentialStore mcpCredentialStore,
    IOptions<AgentRuntimeConfig> runtimeConfig) : IAgentExecutor
{
    private const int MaximumDelegationDepth = 5;

    /// <inheritdoc/>
    public async ValueTask<AgentExecutionResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        var executionStopwatch = Stopwatch.StartNew();
        var events = new ConcurrentQueue<AgentExecutionEvent>();
        var runScope = new AgentRunScope
        {
            MaximumToolInvocations = runtimeConfig.Value.MaximumToolInvocationsPerRun,
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
            tokenCredential: credentialStore.GetTokenCredential(definition.Agent.Provider),
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

        AddUniqueTools(
            tools,
            await builtInTools.CreateAsync(definition, cancellationToken),
            "runtime built-ins",
            logger);

        foreach (var source in definition.Agent.Tools)
        {
            if (source.Service is not null)
            {
                var serviceAgent = definition.Agent with { Tools = [source] };
                AddUniqueTools(
                    tools,
                    AgentExtensions.CreateToolsForAgent(
                        serviceProvider,
                        serviceAgent,
                        deferResolution: true,
                        logger: logger),
                    "service tools",
                    logger);
                continue;
            }

            if (source.Endpoint is not null)
            {
                AddUniqueTools(
                    tools,
                    await CreateRemoteToolsAsync(source, tools, mcpCredentialStore, toolLeases, logger, cancellationToken),
                    "remote MCP tools",
                    logger);
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

    /// <summary>
    /// Connects to a remote MCP source with its tenant credential and returns the tools the definition may use.
    /// The client is added to <paramref name="toolLeases" /> for disposal after the run.
    /// </summary>
    internal static async ValueTask<IEnumerable<AITool>> CreateRemoteToolsAsync(
        ToolSource source,
        IReadOnlyCollection<AITool> composedTools,
        IMcpCredentialStore mcpCredentialStore,
        List<IAsyncDisposable> toolLeases,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string>? headers = null;
        if (source.Credential is { Length: > 0 } credentialName)
        {
            var authorization = await mcpCredentialStore.GetAuthorizationHeaderAsync(
                credentialName,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Remote MCP credential '{credentialName}' is not available to the current tenant.");
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [HeaderNames.Authorization] = authorization,
            };
        }
        McpClient client;
        List<McpClientTool> remoteTools;
        try
        {
            (client, remoteTools) = await AgentExtensions.GetHttpTools(source.Endpoint!, headers, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "{ClassName} could not reach remote MCP source {McpAuthority}; its included tools fail when called",
                nameof(CommonAgentExecutor),
                new Uri(source.Endpoint!).GetLeftPart(UriPartial.Authority));
            return CreateUnavailableTools(source, composedTools);
        }
        toolLeases.Add(client);
        return SelectRemoteTools(remoteTools, source, composedTools, IsReadOnlyRemoteTool, logger);
    }

    /// <summary>
    /// Stands in for the named tools of an unreachable remote source. Each throws when called, so the run
    /// reports a tool failure instead of failing outright; a source without an include list contributes nothing.
    /// </summary>
    internal static IEnumerable<AITool> CreateUnavailableTools(
        ToolSource source,
        IReadOnlyCollection<AITool> composedTools) =>
        RemoveComposedIncludes(source, composedTools).IncludeTools
            .Except(source.ExcludeTools, StringComparer.OrdinalIgnoreCase)
            .Select(toolName => AIFunctionFactory.Create(
                new Func<string>(() => throw new InvalidOperationException(
                    $"Tool '{toolName}' is unavailable because its MCP server could not be reached.")),
                toolName,
                "Currently unavailable because its MCP server could not be reached."));

    /// <summary>
    /// Selects the remote tools a definition may use. Includes already satisfied by composed tools are
    /// dropped; without an explicit include list, only tools the server declares read-only are exposed,
    /// so an actuating tool never reaches a model unless a definition names it.
    /// </summary>
    internal static IEnumerable<AITool> SelectRemoteTools(
        IEnumerable<AITool> remoteTools,
        ToolSource source,
        IReadOnlyCollection<AITool> composedTools,
        Func<AITool, bool> isReadOnly,
        ILogger logger)
    {
        var remoteSource = RemoveComposedIncludes(source, composedTools);
        if (source.IncludeTools.Length > 0 && remoteSource.IncludeTools.Length == 0)
            return [];

        var filtered = AgentExtensions.FilterTools(remoteTools, remoteSource, logger);
        if (remoteSource.IncludeTools.Length > 0)
            return filtered;

        var allowed = new List<AITool>();
        foreach (var tool in filtered)
        {
            if (isReadOnly(tool))
            {
                allowed.Add(tool);
                continue;
            }

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "{ClassName} withheld non-read-only tool {ToolName}; name it in the definition's include list to expose it",
                    nameof(CommonAgentExecutor),
                    tool.Name);
        }
        return allowed;
    }

    internal static bool IsReadOnlyRemoteTool(AITool tool) =>
        tool is McpClientTool { ProtocolTool.Annotations.ReadOnlyHint: true };

    internal static ToolSource RemoveComposedIncludes(ToolSource source, IReadOnlyCollection<AITool> tools)
    {
        if (source.IncludeTools.Length == 0 || tools.Count == 0)
            return source;

        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source with
        {
            IncludeTools = source.IncludeTools
                .Where(toolName => !names.Contains(toolName))
                .ToArray(),
        };
    }

    internal static void AddUniqueTools(
        List<AITool> destination,
        IEnumerable<AITool> candidates,
        string source,
        ILogger logger)
    {
        var names = destination.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in candidates)
        {
            if (names.Add(tool.Name))
            {
                destination.Add(tool);
                continue;
            }

            logger.LogWarning(
                "{ClassName} skipped duplicate tool {ToolName} from {ToolSource}",
                nameof(CommonAgentExecutor),
                tool.Name,
                source);
        }
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
            if (parentScope.Depth >= MaximumDelegationDepth)
                throw new InvalidOperationException($"Agent delegation cannot exceed {MaximumDelegationDepth} levels.");
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
            AgentExecutionResult result;
            try
            {
                result = await ExecuteDefinitionAsync(
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The tool-invocation layer hands this exception to the parent model only, so record it here.
                loggerFactory.CreateLogger<CommonAgentExecutor>().LogError(ex,
                    "{ClassName} delegation to {AgentName} failed at depth {Depth}",
                    nameof(CommonAgentExecutor), agentName, childScope.Depth);
                throw;
            }

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
