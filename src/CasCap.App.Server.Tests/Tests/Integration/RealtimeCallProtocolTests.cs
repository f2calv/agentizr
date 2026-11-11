using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Exercises realtime call setup and hang-up through the controller against a fake Azure realtime endpoint.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RealtimeCallProtocolTests(RealtimeCallProtocolTests.RealtimeWebApplicationFactory factory)
    : IClassFixture<RealtimeCallProtocolTests.RealtimeWebApplicationFactory>
{
    private const string Offer = "v=0 offer";

    [Fact]
    public async Task CreateRealtimeCall_AgentWithoutRealtimeSectionReturnsNull()
    {
        using var httpClient = factory.CreateClient();

        var response = await new AgentRuntimeClient(httpClient)
            .CreateRealtimeCallAsync("assistant", Offer, TestContext.Current.CancellationToken);

        Assert.Null(response);
    }

    [Fact]
    public async Task CreateRealtimeCall_BrokersTheOfferWithTheRuntimeCredential()
    {
        using var httpClient = factory.CreateClient();

        var response = await new AgentRuntimeClient(httpClient)
            .CreateRealtimeCallAsync("tutor", Offer, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(FakeRealtimeHandler.Answer, response.SdpAnswer);
        Assert.DoesNotContain(FakeRealtimeHandler.ProviderCallId, response.CallId, StringComparison.Ordinal);
        var secret = factory.Handler.Latest("client_secrets");
        Assert.Equal(FakeRealtimeHandler.ApiKey, secret.ApiKey);
        using var session = JsonDocument.Parse(secret.Body);
        Assert.Contains("Tutor instructions", session.RootElement.GetProperty("session").GetProperty("instructions").GetString(),
            StringComparison.Ordinal);
        var call = factory.Handler.Latest("calls?webrtcfilter=on");
        Assert.Equal($"Bearer {FakeRealtimeHandler.ClientSecret}", call.Authorization);
        Assert.Equal(Offer, call.Body);
    }

    [Fact]
    public async Task LostControl_HangsUpWithTheRuntimeCredentialAndEndReportsIt()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentRuntimeClient(httpClient);
        var response = await client.CreateRealtimeCallAsync("tutor", Offer, TestContext.Current.CancellationToken);

        // The fake endpoint has no control WebSocket, so the controller loses control and hangs up.
        var hangUp = await factory.Handler.WaitForAsync("/hangup", TimeSpan.FromSeconds(30));
        var summary = await client.EndRealtimeCallAsync("tutor", response!.CallId, TestContext.Current.CancellationToken);

        Assert.Equal(FakeRealtimeHandler.ApiKey, hangUp.ApiKey);
        Assert.Contains(FakeRealtimeHandler.ProviderCallId, hangUp.Path, StringComparison.Ordinal);
        Assert.NotNull(summary);
        Assert.Equal(RealtimeCallEndReason.ControlLost, summary.EndReason);
    }

    [Fact]
    public async Task EndRealtimeCall_UnknownCallReturnsNull()
    {
        using var httpClient = factory.CreateClient();

        var summary = await new AgentRuntimeClient(httpClient)
            .EndRealtimeCallAsync("tutor", "unknown-call", TestContext.Current.CancellationToken);

        Assert.Null(summary);
    }

    /// <summary>Adds a realtime-enabled agent whose provider is a fake Azure realtime endpoint.</summary>
    public sealed class RealtimeWebApplicationFactory : AgentizrWebApplicationFactory
    {
        /// <summary>Gets the fake Azure realtime endpoint.</summary>
        public FakeRealtimeHandler Handler { get; } = new();

        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Nothing listens on the loopback address, so the control WebSocket fails at once.
            const string endpoint = "https://127.0.0.1:1/";
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:realtime:Type"] = nameof(AgentType.AzureOpenAI),
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:realtime:ModelName"] = "gpt-realtime",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:realtime:Endpoint"] = endpoint,
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Providers:realtime:ApiKey"] = FakeRealtimeHandler.ApiKey,
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:tutor:Provider"] = "test",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:tutor:Name"] = "tutor",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:tutor:Description"] = "Test tutor",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:tutor:Prompt"] = "Test prompt",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Agents:tutor:Instructions"] = "Tutor instructions",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Realtime:tutor:Provider"] = "realtime",
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Realtime:tutor:Endpoint"] = endpoint,
                    ["CasCap:AgentRuntimeConfig:Tenants:default:Realtime:tutor:Deployment"] = "gpt-realtime",
                }));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(nameof(AzureRealtimeClient)).ConfigurePrimaryHttpMessageHandler(() => Handler));
        }
    }

    /// <summary>Answers the Azure realtime REST calls and records them; it serves no control WebSocket.</summary>
    public sealed class FakeRealtimeHandler : HttpMessageHandler
    {
        /// <summary>The synthetic resource API key.</summary>
        public const string ApiKey = "example-api-key";

        /// <summary>The synthetic ephemeral client secret.</summary>
        public const string ClientSecret = "ek_example";

        /// <summary>The synthetic provider call identifier.</summary>
        public const string ProviderCallId = "rtc_example";

        /// <summary>The synthetic SDP answer.</summary>
        public const string Answer = "v=0 answer";

        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        /// <summary>Returns the most recent recorded request whose path and query contain the fragment.</summary>
        public RecordedRequest Latest(string fragment) =>
            _requests.LastOrDefault(request => request.Path.Contains(fragment, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No request to '{fragment}' was recorded.");

        /// <summary>Waits until a request whose path contains the fragment has been recorded.</summary>
        public async Task<RecordedRequest> WaitForAsync(string fragment, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_requests.FirstOrDefault(request => request.Path.Contains(fragment, StringComparison.Ordinal)) is { } found)
                    return found;
                await Task.Delay(100);
            }
            throw new TimeoutException($"No request to '{fragment}' within {timeout}.");
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            _requests.Enqueue(new RecordedRequest(
                path,
                request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null,
                request.Headers.Authorization?.ToString(),
                body));

            if (path.EndsWith("/client_secrets", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = ClientSecret }) };
            if (path.Contains("/calls?", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(Answer) };
                response.Headers.Location = new Uri($"/openai/v1/realtime/calls/{ProviderCallId}", UriKind.Relative);
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A request the fake endpoint received.</summary>
    public sealed record RecordedRequest(string Path, string? ApiKey, string? Authorization, string Body);
}
