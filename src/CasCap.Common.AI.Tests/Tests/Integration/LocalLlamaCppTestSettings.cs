namespace CasCap.Common.AI.Tests.Integration;

/// <summary>Environment-provided settings for local llama.cpp integration tests.</summary>
public sealed record LocalLlamaCppTestSettings
{
    private sealed class BasicAuthenticationHandler(string parameter) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", parameter);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Gets the OpenAI-compatible llama.cpp endpoint.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the loaded text model name.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets the optional multimodal model name.</summary>
    public string? VisionModelName { get; init; }

    /// <summary>Gets the optional ingress Basic-auth username.</summary>
    public string? BasicAuthUsername { get; init; }

    /// <summary>Gets the optional ingress Basic-auth password.</summary>
    public string? BasicAuthPassword { get; init; }

    /// <summary>Gets the request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates the endpoint transport. Ingress Basic auth is applied per request because the
    /// OpenAI client overwrites the <c>Authorization</c> header with its bearer API key.
    /// </summary>
    public HttpClient CreateHttpClient()
    {
        HttpMessageHandler handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(BasicAuthUsername))
        {
            var parameter = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{BasicAuthUsername}:{BasicAuthPassword}"));
            handler = new BasicAuthenticationHandler(parameter) { InnerHandler = handler };
        }

        return new HttpClient(handler)
        {
            BaseAddress = Endpoint,
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>Loads settings without persisting or exposing local endpoint details.</summary>
    public static bool TryLoad(out LocalLlamaCppTestSettings? settings)
    {
        var endpointValue = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_ENDPOINT");
        var modelName = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_MODEL");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(modelName))
        {
            settings = null;
            return false;
        }

        var timeout = int.TryParse(
            Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_TIMEOUT_SECONDS"),
            out var timeoutSeconds)
            && timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeSpan.FromMinutes(2);

        settings = new LocalLlamaCppTestSettings
        {
            Endpoint = endpoint,
            ModelName = modelName,
            VisionModelName = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_VISION_MODEL"),
            BasicAuthUsername = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_BASIC_AUTH_USERNAME"),
            BasicAuthPassword = Environment.GetEnvironmentVariable("CASCAP_LOCAL_LLAMA_CPP_BASIC_AUTH_PASSWORD"),
            Timeout = timeout,
        };
        if (string.IsNullOrWhiteSpace(settings.BasicAuthUsername)
            != string.IsNullOrWhiteSpace(settings.BasicAuthPassword))
        {
            settings = null;
            return false;
        }
        return true;
    }
}
