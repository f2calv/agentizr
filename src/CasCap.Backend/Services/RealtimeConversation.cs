using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>Bounded voice-only text context; never stores audio, tool payloads or knowledge evidence.</summary>
internal sealed class RealtimeConversation
{
    private readonly HashSet<string> _truncated = [];

    /// <summary>Gets the definition under which this conversation started.</summary>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets or sets the absolute expiry, extended by a completed utterance only.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets the ordered completed message projections.</summary>
    public JsonArray Messages { get; init; } = [];

    /// <summary>Observes message order before asynchronous transcription completion can reorder speakers.</summary>
    public void Observe(string itemId, string role)
    {
        if (role is "user" or "assistant" && !Messages.Any(item => item!["id"]!.GetValue<string>() == itemId))
            Messages.Add(new JsonObject { ["id"] = itemId, ["role"] = role, ["text"] = string.Empty });
        while (Messages.Count > 40)
            Messages.RemoveAt(0);
    }

    /// <summary>Forgets interrupted assistant audio rather than replaying words the learner may not have heard.</summary>
    public void Truncate(string itemId)
    {
        _truncated.Add(itemId);
        var item = Messages.FirstOrDefault(item => item!["id"]!.GetValue<string>() == itemId);
        if (item is not null)
            Messages.Remove(item);
    }

    /// <summary>Records a completed transcript and bounds the retained context without splitting an utterance.</summary>
    public void Record(string itemId, string role, string text)
    {
        if (_truncated.Contains(itemId) || role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(text))
            return;
        if (text.Length > 16_384)
            throw new InvalidOperationException("The voice utterance exceeds the resumable context limit.");
        var existing = Messages.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == itemId);
        if (existing is not null)
            existing["text"] = text;
        else
            Messages.Add(new JsonObject { ["id"] = itemId, ["role"] = role, ["text"] = text });
        while (Messages.Count > 40 || Messages.Sum(item => item!["text"]!.GetValue<string>().Length) > 32_768)
            Messages.RemoveAt(0);
    }

    /// <summary>Builds text-only provider history; no functions are replayed or executed.</summary>
    public IEnumerable<JsonObject> Replay()
    {
        foreach (var item in Messages)
        {
            if (string.IsNullOrWhiteSpace(item!["text"]!.GetValue<string>()))
                continue;
            var role = item!["role"]!.GetValue<string>();
            yield return new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "message",
                    ["id"] = item["id"]!.GetValue<string>(),
                    ["role"] = role,
                    ["content"] = new JsonArray(new JsonObject
                    {
                        ["type"] = role == "user" ? "input_text" : "output_text",
                        ["text"] = item["text"]!.GetValue<string>(),
                    }),
                },
            };
        }
    }
}
