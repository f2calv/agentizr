namespace CasCap.Client.Constants;

/// <summary>Route templates for the same-origin administration API used by the browser client.</summary>
public static class AdministrationRoutes
{
    /// <summary>Gets the route parameter name that selects the administered app.</summary>
    public const string AppIdParameter = "appId";

    /// <summary>Gets the route parameter name that selects an agent within the app.</summary>
    public const string AgentNameParameter = "agentName";

    /// <summary>Gets the route parameter name that selects an immutable definition version.</summary>
    public const string DefinitionVersionParameter = "definitionVersion";

    /// <summary>Gets the administration API prefix, separate from the machine runtime API.</summary>
    public const string Prefix = "/admin/api";

    /// <summary>Gets the route listing the apps the administrator may manage.</summary>
    public const string Apps = Prefix + "/apps";

    /// <summary>Gets the route of one administered app.</summary>
    public const string App = Apps + "/{" + AppIdParameter + "}";

    /// <summary>Gets the route of the app's provider registrations and definition policy.</summary>
    public const string Providers = App + "/providers";

    /// <summary>Gets the route of the app's paged agent discovery.</summary>
    public const string Agents = App + "/agents";

    /// <summary>Gets the route of one agent within the app.</summary>
    public const string Agent = Agents + "/{" + AgentNameParameter + "}";

    /// <summary>Gets the route for publishing and listing the agent's immutable definitions.</summary>
    public const string Definitions = Agent + "/definitions";

    /// <summary>Gets the route of the agent's active definition.</summary>
    public const string ActiveDefinition = Definitions + "/active";

    /// <summary>Gets the route for deactivating the agent's active definition.</summary>
    public const string DefinitionDeactivation = ActiveDefinition + "/deactivate";

    /// <summary>Gets the route of the agent's activation and rollback history.</summary>
    public const string DefinitionActivations = Definitions + "/activations";

    /// <summary>Gets the route of one immutable definition version.</summary>
    public const string DefinitionVersion = Definitions + "/{" + DefinitionVersionParameter + "}";

    /// <summary>Gets the route for evaluating one immutable definition version.</summary>
    public const string DefinitionEvaluation = DefinitionVersion + "/evaluate";

    /// <summary>Gets the route for activating or rolling back to one immutable definition version.</summary>
    public const string DefinitionActivation = DefinitionVersion + "/activate";

    /// <summary>Builds a request path by escaping and substituting route values into a template.</summary>
    /// <param name="template">One of the route templates in this class.</param>
    /// <param name="appId">The administered app.</param>
    /// <param name="agentName">The agent, when the template selects one.</param>
    /// <param name="definitionVersion">The definition version, when the template selects one.</param>
    public static string Format(
        string template,
        string appId,
        string? agentName = null,
        string? definitionVersion = null)
    {
        var path = template.Replace("{" + AppIdParameter + "}", Uri.EscapeDataString(appId), StringComparison.Ordinal);
        if (agentName is not null)
            path = path.Replace("{" + AgentNameParameter + "}", Uri.EscapeDataString(agentName), StringComparison.Ordinal);
        if (definitionVersion is not null)
            path = path.Replace("{" + DefinitionVersionParameter + "}", Uri.EscapeDataString(definitionVersion), StringComparison.Ordinal);
        return path;
    }
}
