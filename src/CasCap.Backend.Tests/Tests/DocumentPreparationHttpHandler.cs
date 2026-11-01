namespace CasCap.Tests;

/// <summary>Returns a synthetic preparation response for unit and host-integration callers.</summary>
internal sealed class DocumentPreparationHttpHandler : HttpMessageHandler
{
    /// <summary>Gets the captured request body.</summary>
    public string? Body { get; private set; }

    /// <summary>Gets the optional synthetic credential asserted on the request.</summary>
    public string? ExpectedCredential { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ExpectedCredential is not null)
            Assert.Equal(ExpectedCredential, request.Headers.Authorization?.Parameter);
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"resp_synthetic","object":"response","created_at":1,"status":"completed","model":"synthetic-model",
                "output":[{"type":"message","id":"msg_synthetic","status":"completed","role":"assistant",
                "content":[{"type":"output_text","text":"{\"page\":7,\"markdown\":\"synthetic\",\"changes\":[],\"uncertainties\":[]}","annotations":[]}]}],
                "usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}
                """, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
