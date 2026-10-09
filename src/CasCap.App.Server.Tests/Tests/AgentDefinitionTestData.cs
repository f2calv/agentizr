using System.Text.Json;

namespace CasCap.TestData;

/// <summary>Builds secret-free Agent Runtime definitions for unit and protocol tests.</summary>
public static class AgentDefinitionTestData
{
    /// <summary>Creates a complete immutable runtime definition.</summary>
    public static AgentDefinition CreateDefinition(
        string name = "assistant",
        string providerName = "test",
        ToolSource[]? tools = null) =>
        new()
        {
            Name = name,
            Version = "test-v1",
            Agent = CreateAgent(name, providerName, tools),
            Provider = CreateProvider($"{providerName}-model"),
        };

    /// <summary>Creates the agent portion of a definition contract.</summary>
    public static AgentConfig CreateAgent(
        string name = "assistant",
        string providerName = "test",
        ToolSource[]? tools = null) =>
        new()
        {
            Provider = providerName,
            Name = name,
            Description = $"{name} description",
            Prompt = $"{name} prompt",
            Instructions = "Answer test requests.",
            Tools = tools ?? [],
        };

    /// <summary>Creates the secret-free provider portion of a definition contract.</summary>
    public static ProviderConfig CreateProvider(string modelName = "test-model") =>
        new()
        {
            Type = AgentType.Ollama,
            ModelName = modelName,
            Endpoint = new Uri("http://localhost:11434"),
        };

    /// <summary>Creates a versioned publication request using the public wire contract.</summary>
    public static PublishAgentDefinitionRequest CreatePublishRequest(
        string version,
        string modelName = "test-model") =>
        new()
        {
            DefinitionVersion = version,
            SchemaVersion = 1,
            ChangeReason = "test publication",
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = CreateAgent(),
                provider = CreateProvider(modelName),
            }, JsonSerializerOptions.Web),
        };
}