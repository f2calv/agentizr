namespace CasCap.AgentRuntime.Evaluation.Tests.Services;

/// <summary>
/// Builds a deferred in-process tool surface for offline assertions without resolving its backing services.
/// </summary>
/// <param name="toolTypes">Types whose <see cref="McpServerToolAttribute"/> methods become tools.</param>
/// <param name="queryPrefixes">
/// Tool-name prefixes treated as read-only when a tool is not annotated
/// <see cref="McpServerToolAttribute.ReadOnly"/>; defaults to <see cref="DefaultQueryPrefixes"/>.
/// </param>
/// <exception cref="ArgumentException">Two tool types share a simple name.</exception>
/// <remarks>
/// Service sources use the same <see cref="AgentExtensions.CreateToolsFromServiceProvider(IServiceProvider, Type, bool)"/>
/// and <see cref="AgentExtensions.FilterTools"/> path as production service tools, with
/// deferred resolution so no backing service is needed. Endpoint and agent sources are not included.
/// </remarks>
internal sealed class McpToolCatalog(
    IEnumerable<Type> toolTypes,
    IEnumerable<string>? queryPrefixes = null)
{
    private readonly Dictionary<string, Type> _toolTypes = CreateToolTypes(toolTypes);
    private readonly IReadOnlyList<string> _queryPrefixes = queryPrefixes?.ToList() ?? DefaultQueryPrefixes;

    /// <summary>Read-only tool-name prefixes used when a tool carries no <see cref="McpServerToolAttribute.ReadOnly"/> annotation.</summary>
    public static IReadOnlyList<string> DefaultQueryPrefixes { get; } = ["get_", "list_", "search_", "test_", "validate_"];

    /// <summary>
    /// Classifies a tool: delegation tools are named <c>invoke_*</c>; a tool is a query when it is annotated
    /// <see cref="McpServerToolAttribute.ReadOnly"/> or its name starts with a query prefix; anything else is
    /// treated as a side effect.
    /// </summary>
    /// <param name="tool">The tool to classify.</param>
    public ToolDisposition Classify(AIFunction tool)
        => tool.Name.StartsWith("invoke_", StringComparison.Ordinal)
            ? ToolDisposition.Delegation
            : tool.UnderlyingMethod?.GetCustomAttribute<McpServerToolAttribute>()?.ReadOnly == true
                || _queryPrefixes.Any(p => tool.Name.StartsWith(p, StringComparison.Ordinal))
            ? ToolDisposition.Query
            : ToolDisposition.SideEffect;

    /// <summary>Returns every in-process tool across all catalogued types.</summary>
    /// <param name="serviceProvider">Resolves backing services only if a tool is invoked unwrapped; defaults to none.</param>
    public IEnumerable<AIFunction> GetAllTools(IServiceProvider? serviceProvider = null) =>
        _toolTypes.Values.SelectMany(t => CreateTools(serviceProvider ?? EmptyServiceProvider.Instance, t));

    /// <summary>
    /// Returns the service tools of an agent after include and exclude filtering.
    /// </summary>
    /// <param name="serviceProvider">Resolves backing services only if a tool is invoked unwrapped.</param>
    /// <param name="agentConfig">The agent whose tool sources are resolved.</param>
    /// <exception cref="InvalidOperationException">A source names an unknown service or a missing tool.</exception>
    public List<AIFunction> GetServiceTools(IServiceProvider serviceProvider, AgentConfig agentConfig)
    {
        var tools = new List<AIFunction>();
        foreach (var source in agentConfig.Tools.Where(s => s.Service is not null))
        {
            if (!_toolTypes.TryGetValue(source.Service!, out var type))
                throw new InvalidOperationException(
                    $"Agent '{agentConfig.Name}' references unknown tool service '{source.Service}'.");
            tools.AddRange(AgentExtensions.FilterTools(CreateTools(serviceProvider, type), source)
                .Cast<AIFunction>());
        }

        return [.. tools
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => g.First())];
    }

    private static IEnumerable<AIFunction> CreateTools(IServiceProvider serviceProvider, Type type) =>
        AgentExtensions.CreateToolsFromServiceProvider(serviceProvider, type, deferResolution: true).Cast<AIFunction>();

    private static Dictionary<string, Type> CreateToolTypes(IEnumerable<Type> toolTypes)
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in toolTypes)
            if (!types.TryAdd(type.Name, type) && types[type.Name] != type)
                throw new ArgumentException($"Ambiguous tool type name '{type.Name}'.", nameof(toolTypes));
        return types;
    }
}
