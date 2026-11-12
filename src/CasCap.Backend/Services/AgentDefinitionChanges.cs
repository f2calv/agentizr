using System.Text;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Summarizes which properties differ between two stored definition snapshots.</summary>
internal static class AgentDefinitionChanges
{
    /// <summary>The label for a version with no earlier version to compare with.</summary>
    public const string InitialVersion = "initial version";

    private static readonly string[] Sections = ["agent", "provider", "realtime"];

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["agent.name"] = "display name",
        ["agent.prompt"] = "default prompt",
        ["agent.provider"] = "provider registration",
        ["provider.type"] = "provider type",
        ["provider.modelName"] = "model",
        ["provider.reasoningEffort"] = "reasoning effort",
        ["realtime"] = "realtime",
    };

    /// <summary>Returns readable labels of the changed properties, in definition order.</summary>
    /// <param name="previousJson">The previously published snapshot, or <see langword="null" /> for the first version.</param>
    /// <param name="currentJson">The snapshot to describe.</param>
    public static IReadOnlyList<string> Describe(string? previousJson, string currentJson)
    {
        if (previousJson is null)
            return [InitialVersion];
        using var previous = JsonDocument.Parse(previousJson);
        using var current = JsonDocument.Parse(currentJson);
        var changes = new List<string>();
        foreach (var section in Sections)
        {
            var before = GetProperty(previous.RootElement, section);
            var after = GetProperty(current.RootElement, section);
            if (section == "realtime" || before?.ValueKind != JsonValueKind.Object || after?.ValueKind != JsonValueKind.Object)
            {
                if (!AreEqual(before, after))
                    changes.Add(Label(section, null));
                continue;
            }
            foreach (var name in PropertyNames(before.Value, after.Value))
            {
                if (!AreEqual(GetProperty(before.Value, name), GetProperty(after.Value, name)))
                    changes.Add(Label(section, name));
            }
        }
        return changes;
    }

    private static IEnumerable<string> PropertyNames(JsonElement before, JsonElement after) =>
        after.EnumerateObject().Select(property => property.Name)
            .Concat(before.EnumerateObject().Select(property => property.Name))
            .Distinct(StringComparer.Ordinal);

    private static JsonElement? GetProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static bool AreEqual(JsonElement? before, JsonElement? after) =>
        before is null ? after is null : after is not null && JsonElement.DeepEquals(before.Value, after.Value);

    private static string Label(string section, string? name)
    {
        var key = name is null ? section : $"{section}.{name}";
        return Labels.TryGetValue(key, out var label) ? label : Humanize(name ?? section);
    }

    // Splits camelCase names, for example "maxOutputTokens" into "max output tokens".
    private static string Humanize(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        foreach (var character in name)
        {
            if (char.IsUpper(character) && builder.Length > 0)
                builder.Append(' ');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
