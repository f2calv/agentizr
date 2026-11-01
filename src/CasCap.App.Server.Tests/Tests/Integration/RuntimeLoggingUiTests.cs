#if DEBUG
using CasCap.AgentRuntime.Contracts.RuntimeConfiguration;
using CasCap.Client.Constants;
using CasCap.Client.Pages;
using CasCap.Common.Configuration.Management.Contracts.Models;
using System.Net;
using System.Text.Json;
using static CasCap.Tests.Integration.AdministrationComponentTestRenderer;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the actual host page and shared editor without publishing or contacting providers.</summary>
[Trait("Category", "Integration")]
public sealed class RuntimeLoggingUiTests
{
    [Fact]
    public async Task EmbedsTypedDurableDocumentWithoutPublication()
    {
        using var handler = new LoggingHandler(HttpStatusCode.OK);
        var html = await RenderAsync<RuntimeLoggingSettings>(handler, [], string.Empty);
        Assert.Contains("Minimum logging level", html, StringComparison.Ordinal);
        Assert.Contains("whole host", html, StringComparison.Ordinal);
        Assert.Contains("not each replica", html, StringComparison.Ordinal);
        Assert.Contains("9007199254740993", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Debug\" selected", html, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RefusesEditingWhenDestinationIsUnavailable(HttpStatusCode status)
    {
        using var handler = new LoggingHandler(status);
        var html = await RenderAsync<RuntimeLoggingSettings>(handler, [], string.Empty);
        Assert.DoesNotContain("Minimum logging level", html, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("Loading logging destination", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DurableWireAndBrowserAssemblyStayCompatible()
    {
        var controls = JsonSerializer.Deserialize<RuntimeLoggingControls>("""{"MinimumLevel":"Information"}""");
        Assert.NotNull(controls);
        Assert.Equal(RuntimeLoggingLevel.Information, controls.MinimumLevel);
        Assert.Equal("""{"MinimumLevel":"Information"}""", JsonSerializer.Serialize(controls));
        Assert.DoesNotContain(typeof(RuntimeLoggingControls).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is not null
                && (assembly.Name.StartsWith("Serilog", StringComparison.Ordinal)
                    || assembly.Name.Contains("EntityFrameworkCore", StringComparison.Ordinal)));
    }

    private sealed class LoggingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Rendering must not mutate logging.");
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
                response.Content = request.RequestUri.AbsolutePath == "/" + RuntimeLoggingRoutes.Destination
                    ? JsonContent.Create(new RuntimeConfigurationDestination("sample-application", "sample-environment", "logging"))
                    : JsonContent.Create(new RuntimeConfigurationDocument<RuntimeLoggingControls>(
                        9007199254740993, new() { MinimumLevel = RuntimeLoggingLevel.Debug },
                        DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow.AddMinutes(-1)));
            return Task.FromResult(response);
        }
    }
}
#endif
