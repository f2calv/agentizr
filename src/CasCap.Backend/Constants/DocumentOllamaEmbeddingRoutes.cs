#if DEBUG
namespace CasCap.Constants;

/// <summary>Native Ollama metadata and embedding operations; no model-management routes are exposed.</summary>
internal static class DocumentOllamaEmbeddingRoutes
{
    /// <summary>Lists installed model identities and digests.</summary>
    public const string Tags = "api/tags";
    /// <summary>Reads model capabilities and native dimensions without loading it for inference.</summary>
    public const string Show = "api/show";
    /// <summary>Submits an ordered batch of embedding inputs.</summary>
    public const string Embed = "api/embed";
}
#endif
