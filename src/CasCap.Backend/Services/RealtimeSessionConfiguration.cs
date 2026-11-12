using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>Builds the GA Realtime session configuration from an agent definition.</summary>
internal static class RealtimeSessionConfiguration
{
    /// <summary>Builds the session object for a client secret, or for <c>session.update</c> without the model.</summary>
    /// <param name="definition">The active definition; its inline instructions become the session instructions.</param>
    /// <param name="tools">The allowlisted tools the call may execute.</param>
    /// <param name="includeModel">Whether to include the deployment, which only a client secret may set.</param>
    public static JsonObject Build(AgentDefinition definition, IReadOnlyCollection<AIFunction> tools, bool includeModel)
    {
        var realtime = definition.Realtime
            ?? throw new InvalidOperationException($"Agent '{definition.Name}' has no realtime configuration.");

        var turnDetection = new JsonObject
        {
            ["type"] = realtime.TurnDetection,
            ["create_response"] = true,
            ["interrupt_response"] = true,
        };
        if (realtime.TurnDetection == "semantic_vad")
            turnDetection["eagerness"] = realtime.Eagerness;

        var input = new JsonObject { ["turn_detection"] = turnDetection };
        if (!string.IsNullOrWhiteSpace(realtime.TranscriptionModel))
        {
            var transcription = new JsonObject { ["model"] = realtime.TranscriptionModel };
            if (!string.IsNullOrWhiteSpace(realtime.TranscriptionLanguage))
                transcription["language"] = realtime.TranscriptionLanguage;
            input["transcription"] = transcription;
        }

        var session = new JsonObject { ["type"] = "realtime" };
        if (includeModel)
            session["model"] = realtime.Deployment;
        session["instructions"] = definition.Agent.Instructions;
        session["output_modalities"] = new JsonArray("audio");
        session["audio"] = new JsonObject
        {
            ["input"] = input,
            ["output"] = new JsonObject { ["voice"] = realtime.Voice },
        };
        session["tools"] = new JsonArray([.. tools.Select(tool => (JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText()),
        })]);
        session["tool_choice"] = tools.Count > 0 ? "auto" : "none";
        return session;
    }
}
