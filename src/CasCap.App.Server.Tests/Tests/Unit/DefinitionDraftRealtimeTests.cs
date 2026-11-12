using CasCap.Client.Models;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies that editor drafts carry the optional realtime section without dropping or inventing it.</summary>
[Trait("Category", "Generation")]
public sealed class DefinitionDraftRealtimeTests
{
    private const string Definition = """
        {
          "agent": { "name": "Tutor", "provider": "example", "tools": [] },
          "provider": { "type": "AzureOpenAI", "modelName": "example-model" },
          "realtime": { "provider": "example-realtime", "deployment": "example-deployment" }
        }
        """;

    [Fact]
    public void UneditedRealtimeSection_IsPreserved()
    {
        var draft = DefinitionDraft.FromDefinition(JsonDocument.Parse(Definition).RootElement);
        var form = draft.ToForm("tutor");

        Assert.True(draft.TryCreatePayload(form, out var payload, out var error), error);
        Assert.Equal("example-deployment", payload.GetProperty("realtime").GetProperty("deployment").GetString());
    }

    [Fact]
    public void BlankRealtimeSection_IsRemoved()
    {
        var draft = DefinitionDraft.FromDefinition(JsonDocument.Parse(Definition).RootElement);
        var form = draft.ToForm("tutor");
        form.RealtimeJson = " ";

        Assert.True(draft.TryCreatePayload(form, out var payload, out var error), error);
        Assert.False(payload.TryGetProperty("realtime", out _));
    }

    [Theory]
    [InlineData("[]", "Realtime must be a JSON object.")]
    [InlineData("{", "Realtime must be a JSON object:")]
    public void InvalidRealtimeSection_IsRejected(string realtimeJson, string expectedError)
    {
        var draft = DefinitionDraft.Empty();
        var form = draft.ToForm("tutor");
        form.RealtimeJson = realtimeJson;

        Assert.False(draft.TryCreatePayload(form, out _, out var error));
        Assert.StartsWith(expectedError, error, StringComparison.Ordinal);
    }
}
