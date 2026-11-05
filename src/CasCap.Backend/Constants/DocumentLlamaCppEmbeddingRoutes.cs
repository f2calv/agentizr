namespace CasCap.Constants;

/// <summary>Read-only single-model llama.cpp metadata and one non-retried embedding operation.</summary>
internal static class DocumentLlamaCppEmbeddingRoutes
{
    /// <summary>Binds the advertised server model alias to the operator-verified GGUF digest.</summary>
    public const string ModelAliasPrefix = "sha256-";

    /// <summary>Reads the loaded model alias and native dimensions without requesting a model load.</summary>
    public const string Models = "v1/models";

    /// <summary>Reads the selected single-model server identity without router selection.</summary>
    public const string Properties = "props";

    /// <summary>Submits exact input strings to the OpenAI-compatible embedding endpoint.</summary>
    public const string Embed = "v1/embeddings";
}
