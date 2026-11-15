using System.Security.Claims;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies bounded voice replay, interruption safety and validated-caller separation without services.</summary>
[Trait("Category", "Realtime")]
public sealed class RealtimeConversationTests
{
    [Fact]
    public void Replay_PreservesMessageOrderWhenTranscriptionCompletesOutOfOrder()
    {
        var context = new RealtimeConversation { DefinitionVersion = "1" };
        context.Observe("item_user", "user");
        context.Observe("item_tutor", "assistant");
        context.Record("item_tutor", "assistant", "Synthetic answer");
        context.Record("item_user", "user", "Synthetic question");
        context.Record("item_user", "user", "Synthetic question");

        var items = context.Replay().ToArray();

        Assert.Equal(2, items.Length);
        Assert.Equal("user", items[0]["item"]!["role"]!.GetValue<string>());
        Assert.Equal("Synthetic question", items[0]["item"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("input_text", items[0]["item"]!["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("output_text", items[1]["item"]!["content"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Replay_DoesNotReintroduceInterruptedAudioOrUncompletedMessages()
    {
        var context = new RealtimeConversation { DefinitionVersion = "1" };
        context.Record("item_user", "user", "Synthetic question");
        context.Observe("item_pending", "user");
        context.Record("item_tutor", "assistant", "Words not necessarily heard");
        context.Truncate("item_tutor");
        context.Record("item_tutor", "assistant", "Late transcript after interruption");

        Assert.Equal("user", Assert.Single(context.Replay())["item"]!["role"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(60, 10, 40)]
    [InlineData(5, 10_000, 3)]
    public void Record_BoundsBothUtteranceCountAndTotalCharacters(int count, int length, int expected)
    {
        var context = new RealtimeConversation { DefinitionVersion = "1" };
        for (var index = 0; index < count; index++)
            context.Record($"item_{index}", "user", new string('a', length));

        Assert.Equal(expected, context.Replay().Count());
        Assert.EndsWith($"_{count - expected}", context.Messages[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Record_RejectsAnOversizedUtteranceInsteadOfSilentlyTruncatingIt()
    {
        var context = new RealtimeConversation { DefinitionVersion = "1" };
        Assert.Throws<InvalidOperationException>(() => context.Record("item_user", "user", new string('a', 16_385)));
        Assert.Empty(context.Replay());
    }

    [Fact]
    public void Serialization_RoundTripsOnlyTheExplicitVoiceProjection()
    {
        var context = new RealtimeConversation
        {
            DefinitionVersion = "version-1",
            ExpiresAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        };
        context.Record("item_user", "user", "Synthetic question");
        context.Record("item_tool", "function", "Not retained");

        var json = JsonSerializer.Serialize(context);
        var restored = JsonSerializer.Deserialize<RealtimeConversation>(json)!;

        Assert.Single(restored.Replay());
        Assert.Equal(context.ExpiresAt, restored.ExpiresAt);
        Assert.Equal("version-1", restored.DefinitionVersion);
        Assert.DoesNotContain("Not retained", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionId_BindsConversationToAuthenticatedIssuerAndSubject()
    {
        var id = Guid.NewGuid();
        var owner = Caller("issuer-a", "subject-a");
        var key = RealtimeConversationStore.GetSessionId(owner, id);

        Assert.NotNull(key);
        Assert.Equal(key, RealtimeConversationStore.GetSessionId(owner, id));
        Assert.NotEqual(key, RealtimeConversationStore.GetSessionId(Caller("issuer-a", "subject-b"), id));
        Assert.NotEqual(key, RealtimeConversationStore.GetSessionId(Caller("issuer-b", "subject-a"), id));
        Assert.Null(RealtimeConversationStore.GetSessionId(new ClaimsPrincipal(), id));
        Assert.Null(RealtimeConversationStore.GetSessionId(Caller("issuer-a", null), id));
        Assert.DoesNotContain("subject-a", key, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal Caller(string issuer, string? subject)
    {
        var identity = new ClaimsIdentity("synthetic");
        identity.AddClaim(new Claim("iss", issuer));
        if (subject is not null)
            identity.AddClaim(new Claim("sub", subject));
        return new(identity);
    }
}
