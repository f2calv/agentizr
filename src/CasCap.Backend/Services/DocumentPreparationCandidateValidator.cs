using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CasCap.Services;

/// <summary>Enforces the complete local response/page/usage contract independently of provider schema enforcement.</summary>
internal static partial class DocumentPreparationCandidateValidator
{
    private static readonly string[] RequiredProperties = ["page", "markdown", "changes", "uncertainties"];

    /// <summary>Rejects extra/duplicate fields, wrong pages, missing completion and invalid counters without approving content quality.</summary>
    public static DocumentPreparationCandidate Validate(
        AgentStructuredResponse response,
        DocumentPreparationPlan plan,
        int page,
        int maximumCharacters
    )
    {
        if (!response.IsComplete || string.IsNullOrWhiteSpace(response.ResponseId)
            || response.Text.Length > maximumCharacters
            || response.ModelId != plan.Settings.Model && response.ModelId != plan.Settings.Model + "-" + plan.Settings.ModelRevision
            || response.Usage?.InputTokenCount is < 0 || response.Usage?.OutputTokenCount is < 0
            || response.Usage?.OutputTokenCount > plan.Settings.MaximumOutputTokens
            || response.Usage?.TotalTokenCount is < 0
            || response.Usage?.AdditionalCounts?.Values.Any(count => count < 0) is true
            || response.Usage is { InputTokenCount: { } input, OutputTokenCount: { } output, TotalTokenCount: { } total }
                && (input > long.MaxValue - output || total != input + output))
            throw new InvalidDataException("The preparation response violates the completion, model, size or usage contract.");
        using var document = JsonDocument.Parse(response.Text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The preparation response is not a page object.");
        var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != RequiredProperties.Length
            || RequiredProperties.Any(property => properties.Count(name => name == property) != 1)
            || root.GetProperty("page").ValueKind != JsonValueKind.Number
            || !root.GetProperty("page").TryGetInt32(out var returnedPage) || returnedPage != page
            || root.GetProperty("markdown").ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The preparation response violates the exact physical-page contract.");
        var markdown = NormalizeMarkdown(root.GetProperty("markdown").GetString()!);
        return new(page, markdown, ReadNotes(root, "changes"), ReadNotes(root, "uncertainties"));
    }

    private static string NormalizeMarkdown(string markdown) =>
        HtmlEntity().Replace(markdown.ReplaceLineEndings("\n"), match =>
        {
            var decoded = WebUtility.HtmlDecode(match.Value);
            return decoded.All(char.IsWhiteSpace) ? decoded : match.Value;
        }).Replace('\u00a0', ' ').Replace('\u2007', ' ').Replace('\u202f', ' ');

    [GeneratedRegex(@"&(?:[a-zA-Z][a-zA-Z0-9]+|#[0-9]+|#[xX][0-9a-fA-F]+);", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlEntity();

    private static string[] ReadNotes(JsonElement root, string property)
    {
        var array = root.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array || array.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new InvalidDataException("Preparation review notes must be string arrays.");
        return array.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }
}
