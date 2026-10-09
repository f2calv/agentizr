using CasCap;
using System.Net.Http.Headers;

namespace CasCap.IntegrationTests.Local;

/// <summary>Exercises a complete running Agent Runtime through its published client.</summary>
[Trait("Category", "Local Agent Runtime")]
public sealed class LiveAgentRuntimeSmokeTests
{
    [LocalAgentRuntimeFact]
    public async Task RuntimeLifecycle_RunStreamInspectReset()
    {
        var configured = LiveAgentRuntimeTestSettings.TryLoad(out var settings);
        Assert.SkipUnless(
            configured,
            "Set the CASCAP_AGENTRUNTIME_LIVE_BASE_ADDRESS and CASCAP_AGENTRUNTIME_LIVE_AGENT_NAME environment variables.");
        using var httpClient = CreateHttpClient(settings!);
        var client = new AgentRuntimeClient(httpClient);
        var sessionId = $"local-smoke-{Guid.NewGuid():N}";
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionCreated = false;

        try
        {
            var response = await client.RunAgentAsync(
                settings!.AgentName,
                new RunAgentRequest
                {
                    SessionId = sessionId,
                    Input = "Reply with exactly the word pong.",
                },
                cancellationToken);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.OutputText));
            Assert.False(string.IsNullOrWhiteSpace(response.DefinitionVersion));
            Assert.False(string.IsNullOrWhiteSpace(response.ModelName));
            sessionCreated = true;

            var session = await client.GetSessionAsync(settings.AgentName, sessionId, cancellationToken);
            Assert.NotNull(session);
            Assert.True(session.SessionEnabled);
            Assert.True(session.Exists);

            var streamItems = new List<RunAgentStreamItem>();
            await foreach (var item in client.StreamAgentAsync(
                settings.AgentName,
                new RunAgentRequest
                {
                    SessionId = sessionId,
                    Input = "Reply with exactly the word stream.",
                },
                cancellationToken))
            {
                streamItems.Add(item);
            }

            Assert.NotEmpty(streamItems);
            Assert.NotNull(streamItems.Last().Response);
            Assert.False(string.IsNullOrWhiteSpace(streamItems.Last().Response!.OutputText));
        }
        finally
        {
            if (sessionCreated)
            {
                using var cleanupCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await client.ResetSessionAsync(
                    settings!.AgentName,
                    sessionId,
                    cleanupCancellationTokenSource.Token);
            }
        }
    }

    private static HttpClient CreateHttpClient(LiveAgentRuntimeTestSettings settings)
    {
        HttpMessageHandler handler = new HttpClientHandler();
        if (settings.Authentication is { } authentication)
        {
            handler = new TokenCredentialBearerHandler(authentication.TokenCredential, authentication.Scope!)
            {
                InnerHandler = handler,
            };
        }

        return new HttpClient(handler)
        {
            BaseAddress = settings.BaseAddress,
            Timeout = settings.Timeout,
            DefaultRequestHeaders =
            {
                Accept = { new MediaTypeWithQualityHeaderValue("application/json") },
            },
        };
    }
}