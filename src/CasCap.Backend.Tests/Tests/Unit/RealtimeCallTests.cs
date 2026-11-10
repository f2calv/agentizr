using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Tests realtime session configuration, tool dispatch and provider brokering without a live service.</summary>
[Trait("Category", "Realtime")]
public sealed class RealtimeCallTests
{
    [Fact]
    public void Build_ClientSecretSessionCarriesDefinitionInstructionsModelAndTools()
    {
        var tool = AIFunctionFactory.Create(([Description("Word")] string term) => term, "add_vocabulary", "Adds a word.");

        var session = RealtimeSessionConfiguration.Build(CreateDefinition(), [tool], includeModel: true);

        Assert.Equal("realtime", session["type"]!.GetValue<string>());
        Assert.Equal("gpt-realtime", session["model"]!.GetValue<string>());
        Assert.Equal("Tutor in German.", session["instructions"]!.GetValue<string>());
        Assert.Equal("semantic_vad", session["audio"]!["input"]!["turn_detection"]!["type"]!.GetValue<string>());
        Assert.Equal("low", session["audio"]!["input"]!["turn_detection"]!["eagerness"]!.GetValue<string>());
        Assert.Equal("de", session["audio"]!["input"]!["transcription"]!["language"]!.GetValue<string>());
        Assert.Equal("marin", session["audio"]!["output"]!["voice"]!.GetValue<string>());
        var declared = Assert.Single(session["tools"]!.AsArray());
        Assert.Equal("add_vocabulary", declared!["name"]!.GetValue<string>());
        Assert.Equal("auto", session["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public void Build_SessionUpdateOmitsModelAndDisablesToolsWhenNoneAreAllowed()
    {
        var session = RealtimeSessionConfiguration.Build(CreateDefinition(), [], includeModel: false);

        Assert.False(session.ContainsKey("model"));
        Assert.Empty(session["tools"]!.AsArray());
        Assert.Equal("none", session["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeAsync_ExecutesAllowlistedToolOncePerCallId()
    {
        var invocations = 0;
        var tool = AIFunctionFactory.Create((string term) => { invocations++; return $"saved {term}"; }, "add_vocabulary");
        var dispatcher = new RealtimeToolDispatcher([tool], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var first = await dispatcher.InvokeAsync("call-1", "add_vocabulary", """{"term":"Haus"}""", TestContext.Current.CancellationToken);
        var repeated = await dispatcher.InvokeAsync("call-1", "add_vocabulary", """{"term":"Haus"}""", TestContext.Current.CancellationToken);

        Assert.Equal("\"saved Haus\"", first);
        Assert.Null(repeated);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task InvokeAsync_RefusesToolsOutsideTheAllowlist()
    {
        var dispatcher = new RealtimeToolDispatcher([], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var output = await dispatcher.InvokeAsync("call-1", "record_mistake", "{}", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(output!);
        Assert.Contains("not available", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonObjectArguments()
    {
        var tool = AIFunctionFactory.Create(() => "ok", "get_learner_profile");
        var dispatcher = new RealtimeToolDispatcher([tool], TimeSpan.FromSeconds(5), NullLogger.Instance);

        var output = await dispatcher.InvokeAsync("call-1", "get_learner_profile", "[1]", TestContext.Current.CancellationToken);

        Assert.Contains("error", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCallAsync_PostsOfferWithClientSecretAndReturnsProviderCallId()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync();
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("v=0 answer") };
            response.Headers.Location = new Uri("/v1/realtime/calls/rtc_example", UriKind.Relative);
            return response;
        });
        var client = new AzureRealtimeClient(new HttpClient(handler));

        var (answer, callId) = await client.CreateCallAsync(
            new Uri("https://example.openai.azure.com/"), "ek_example", "v=0 offer", TestContext.Current.CancellationToken);

        Assert.Equal("v=0 answer", answer);
        Assert.Equal("rtc_example", callId);
        Assert.Equal("https://example.openai.azure.com/openai/v1/realtime/calls?webrtcfilter=on", sent!.RequestUri!.ToString());
        Assert.Equal("Bearer ek_example", sent.Headers.Authorization!.ToString());
        Assert.Equal("application/sdp", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("v=0 offer", body);
    }

    [Fact]
    public async Task CreateClientSecretAsync_FailureReportsStatusWithoutResponseBody()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("private provider detail"),
        }));
        var client = new AzureRealtimeClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateClientSecretAsync(
            new Uri("https://example.openai.azure.com/"),
            RealtimeProviderCredential.FromApiKey("example-key"),
            [],
            TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.DoesNotContain("private provider detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetControlUri_UsesSecureWebSocketWithCallId()
    {
        var uri = AzureRealtimeClient.GetControlUri(new Uri("https://example.openai.azure.com/"), "rtc_example");

        Assert.Equal("wss://example.openai.azure.com/openai/v1/realtime?call_id=rtc_example", uri.ToString());
    }

    [Theory]
    [InlineData("vad", "low")]
    [InlineData("semantic_vad", "eager")]
    public void Validate_RejectsUnknownTurnDetectionSettings(string turnDetection, string eagerness)
    {
        var config = CreateDefinition().Realtime! with { TurnDetection = turnDetection, Eagerness = eagerness };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        Assert.False(valid);
    }

    private static AgentDefinition CreateDefinition() => new()
    {
        Name = "tutor",
        Version = "1",
        Agent = new AgentConfig
        {
            Name = "Tutor",
            Description = "Tutor",
            Prompt = "Start",
            Provider = "chat",
            Instructions = "Tutor in German.",
        },
        Provider = new ProviderConfig { Type = AgentType.AzureOpenAI, ModelName = "chat-model" },
        Realtime = new AgentRealtimeConfig
        {
            Provider = "realtime",
            Endpoint = new Uri("https://example.openai.azure.com/"),
            Deployment = "gpt-realtime",
            Eagerness = "low",
            TranscriptionModel = "whisper-1",
            TranscriptionLanguage = "de",
            Tools = ["add_vocabulary"],
        },
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
