#if DEBUG
using Azure.Core;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Checks read-only metadata binding with fake HTTP and credentials; never connects to Azure.</summary>
[Trait("Category", "HybridPreparation")]
public sealed class DocumentPreparationModelVerifierTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("dictionary")]
    [InlineData("account")]
    [InlineData("endpoint")]
    [InlineData("resource")]
    [InlineData("model")]
    [InlineData("revision")]
    [InlineData("state")]
    public async Task VerifiesActualResourceAndModelBinding(string variation)
    {
        var plan = DocumentHybridPreparationTestData.Plan;
        using var handler = new MetadataHandler(variation);
        using var client = new HttpClient(handler);
        var credential = new Credential();
        var verifier = new DocumentPreparationModelVerifier(new Clients(client), new RuntimeEntraCredential(() => credential));
        if (variation is "valid" or "dictionary")
            await verifier.VerifyAsync(plan, TestContext.Current.CancellationToken);
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(plan, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests);
        Assert.Equal("https://management.azure.com/.default", Assert.Single(credential.Scopes!));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    public async Task MetadataFailureIsExplicitAndRedacted(string variation)
    {
        using var handler = new MetadataHandler(variation);
        using var client = new HttpClient(handler);
        var verifier = new DocumentPreparationModelVerifier(new Clients(client), new RuntimeEntraCredential(() => new Credential()));
        var exception = await Record.ExceptionAsync(() =>
            verifier.VerifyAsync(DocumentHybridPreparationTestData.Plan, TestContext.Current.CancellationToken));
        Assert.NotNull(exception);
        Assert.DoesNotContain("private-response-body", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task CancellationPreventsAnyMetadataRead()
    {
        using var handler = new MetadataHandler("valid");
        using var client = new HttpClient(handler);
        var verifier = new DocumentPreparationModelVerifier(new Clients(client), new RuntimeEntraCredential(() => new Credential()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(DocumentHybridPreparationTestData.Plan, cancellation.Token));
        Assert.Equal(0, handler.Requests);
    }

    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Credential : TokenCredential
    {
        public string[]? Scopes { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes = requestContext.Scopes;
            return new("synthetic-token", DateTimeOffset.MaxValue);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class MetadataHandler(string variation) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("management.azure.com", request.RequestUri!.Host);
            Assert.Equal("?api-version=2024-10-01", request.RequestUri.Query);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var plan = DocumentHybridPreparationTestData.Plan;
            var isDeployment = request.RequestUri.AbsolutePath.Contains("/deployments/", StringComparison.Ordinal);
            var resource = isDeployment ? plan.Settings.DeploymentResourceId
                : plan.Settings.DeploymentResourceId[..plan.Settings.DeploymentResourceId.LastIndexOf("/deployments/", StringComparison.Ordinal)];
            var endpoint = variation == "endpoint" ? "https://different.example.com/" : plan.Definition.Provider.Endpoint!.AbsoluteUri;
            var properties = isDeployment
                ? JsonSerializer.SerializeToElement(new
                {
                    provisioningState = variation == "state" ? "Updating" : "Succeeded",
                    model = new
                    {
                        format = "OpenAI",
                        name = variation == "model" ? "wrong" : plan.Settings.Model,
                        version = variation == "revision" ? "wrong" : plan.Settings.ModelRevision,
                    },
                })
                : JsonSerializer.SerializeToElement(new
                {
                    provisioningState = "Succeeded",
                    endpoint = variation == "dictionary" ? "https://different.example.com/" : endpoint,
                    endpoints = new { OpenAI = endpoint },
                });
            var json = JsonSerializer.Serialize(new
            {
                id = variation == "account" && !isDeployment || variation == "resource" && isDeployment ? "wrong" : resource,
                name = plan.Definition.Provider.ModelName,
                properties,
            });
            if (variation is "malformed" or "error")
                json = "private-response-body";
            if (variation == "oversized")
                json = new string('x', 1_048_577);
            return Task.FromResult(new HttpResponseMessage(variation == "error" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
#endif
