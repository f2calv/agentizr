using Microsoft.Extensions.AI;

namespace CasCap.Services;

/// <summary>
/// Starts and ends brokered realtime voice calls for the authenticated tenant: resolves the active definition,
/// its allowlisted tools and provider credential, negotiates the WebRTC call and hands it to a controller.
/// </summary>
public sealed class RealtimeCallService
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    private readonly IAgentDefinitionStore definitionStore;
    private readonly IProviderCredentialStore credentialStore;
    private readonly IMcpCredentialStore mcpCredentialStore;
    private readonly ITenantContext tenantContext;
    private readonly AzureRealtimeClient azure;
    private readonly RealtimeCallRegistry registry;
    private readonly ILoggerFactory loggerFactory;

    internal RealtimeCallService(
        IAgentDefinitionStore definitionStore,
        IProviderCredentialStore credentialStore,
        IMcpCredentialStore mcpCredentialStore,
        ITenantContext tenantContext,
        AzureRealtimeClient azure,
        RealtimeCallRegistry registry,
        ILoggerFactory loggerFactory)
    {
        this.definitionStore = definitionStore;
        this.credentialStore = credentialStore;
        this.mcpCredentialStore = mcpCredentialStore;
        this.tenantContext = tenantContext;
        this.azure = azure;
        this.registry = registry;
        this.loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Starts a call from a client SDP offer, or returns <see langword="null"/> when the agent is unavailable or
    /// offers no realtime calls.
    /// </summary>
    public async ValueTask<RealtimeCallOutcome?> CreateAsync(string agentName, string sdpOffer, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(agentName, cancellationToken);
        if (definition?.Realtime is not { } realtime)
            return null;
        if (!registry.HasCapacity)
            return new RealtimeCallOutcome { IsAtCapacity = true };

        var logger = loggerFactory.CreateLogger<RealtimeCallService>();
        var credential = await GetCredentialAsync(realtime.Provider, cancellationToken);
        if (credential is null)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("{ClassName} found no credential for realtime provider {ProviderName}",
                    nameof(RealtimeCallService), realtime.Provider);
            return new RealtimeCallOutcome { IsProviderUnavailable = true };
        }

        var toolLeases = new List<IAsyncDisposable>();
        var transferred = false;
        try
        {
            var tools = await LoadToolsAsync(definition, realtime, toolLeases, logger, cancellationToken);
            var secret = await azure.CreateClientSecretAsync(
                realtime.Endpoint,
                credential,
                RealtimeSessionConfiguration.Build(definition, tools, includeModel: true),
                cancellationToken);
            var (sdpAnswer, providerCallId) = await azure.CreateCallAsync(realtime.Endpoint, secret, sdpOffer, cancellationToken);

            var call = new RealtimeCallSession(
                new RealtimeCallSessionOptions
                {
                    CallId = Guid.NewGuid().ToString("N"),
                    TenantId = tenantContext.TenantId,
                    AgentName = agentName,
                    Endpoint = realtime.Endpoint,
                    ProviderCallId = providerCallId,
                    Credential = credential,
                    Session = RealtimeSessionConfiguration.Build(definition, tools, includeModel: false),
                    MaximumDuration = TimeSpan.FromMinutes(realtime.MaximumDurationMinutes),
                },
                azure,
                new RealtimeToolDispatcher(tools, ToolTimeout, loggerFactory.CreateLogger<RealtimeToolDispatcher>()),
                toolLeases,
                loggerFactory.CreateLogger<RealtimeCallSession>());
            transferred = true;
            if (!registry.TryAdd(call))
            {
                // Started by the provider but not controllable here: hang it up rather than leave it uncontrolled.
                call.Start();
                await call.DisposeAsync();
                return new RealtimeCallOutcome { IsAtCapacity = true };
            }
            call.Start();
            return new RealtimeCallOutcome { CallId = call.CallId, SdpAnswer = sdpAnswer };
        }
        catch (HttpRequestException ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(ex, "{ClassName} could not start a realtime call for agent {AgentName}",
                    nameof(RealtimeCallService), agentName);
            return new RealtimeCallOutcome { IsProviderUnavailable = true };
        }
        finally
        {
            if (!transferred)
                foreach (var lease in toolLeases)
                    await lease.DisposeAsync();
        }
    }

    /// <summary>Ends a call owned by the tenant and agent, returning <see langword="false"/> when it is not active here.</summary>
    public async ValueTask<bool> EndAsync(string agentName, string callId)
    {
        if (registry.Find(tenantContext.TenantId, agentName, callId) is not { } call)
            return false;
        await call.EndAsync();
        return true;
    }

    private async ValueTask<RealtimeProviderCredential?> GetCredentialAsync(string providerName, CancellationToken cancellationToken)
    {
        if (credentialStore.GetTokenCredential(providerName) is { } tokenCredential)
            return RealtimeProviderCredential.FromTokenCredential(tokenCredential);
        return await credentialStore.GetApiKeyAsync(providerName, cancellationToken) is { Length: > 0 } apiKey
            ? RealtimeProviderCredential.FromApiKey(apiKey)
            : null;
    }

    private async ValueTask<List<AIFunction>> LoadToolsAsync(
        AgentDefinition definition,
        AgentRealtimeConfig realtime,
        List<IAsyncDisposable> toolLeases,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var allowed = realtime.Tools.ToHashSet(StringComparer.Ordinal);
        var tools = new List<AITool>();
        if (allowed.Count == 0)
            return [];

        // A call can use only tools the text agent itself exposes, narrowed further by the realtime allowlist.
        foreach (var source in definition.Agent.Tools.Where(source => source.Endpoint is not null))
        {
            CommonAgentExecutor.AddUniqueTools(
                tools,
                await CommonAgentExecutor.CreateRemoteToolsAsync(
                    definition.Agent.Name,
                    source,
                    tools,
                    mcpCredentialStore,
                    toolLeases,
                    logger,
                    cancellationToken),
                "remote MCP tools",
                logger);
        }

        var selected = tools.OfType<AIFunction>().Where(tool => allowed.Contains(tool.Name)).ToList();
        foreach (var missing in allowed.Except(selected.Select(tool => tool.Name), StringComparer.Ordinal))
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("{ClassName} realtime tool {ToolName} is not exposed by the agent's MCP sources",
                    nameof(RealtimeCallService), missing);
        }
        return selected;
    }
}
