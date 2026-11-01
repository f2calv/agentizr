namespace CasCap.Constants;

/// <summary>Supported runtime roles in the common Agentizr artifact.</summary>
public static class FeatureNames
{
    /// <summary>Serves the runtime API, administration frontend and optional MCP endpoint.</summary>
    public const string Agentizr = "agentizr";

    /// <summary>Applies migrations and exits without starting a listener or worker.</summary>
    public const string Migrate = "migrate";

    /// <summary>Admits document events under distributed leadership.</summary>
    public const string Ingestion = "ingestion";
}
