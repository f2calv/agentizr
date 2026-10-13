namespace CasCap.Common.Models;

/// <summary>
/// The AI provider type (e.g. Ollama, OpenAI).
/// </summary>
public enum AgentType
{
    /// <summary>
    /// <see href="https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai?pivots=programming-language-csharp"/>
    /// </summary>
    AzureOpenAI = 1,
    /// <summary>
    /// <see href="https://learn.microsoft.com/en-us/agent-framework/agents/providers/ollama?pivots=programming-language-csharp"/>
    /// </summary>
    Ollama = 3,
    /// <summary>
    /// <see href="https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai?pivots=programming-language-csharp"/>
    /// </summary>
    OpenAI = 4,
}
