using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Pages;
using CasCap.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace CasCap.Tests.Integration;

/// <summary>Renders definition pages against synthetic HTTP responses without starting the application or providers.</summary>
[Trait("Category", "Integration")]
public sealed class AdministrationDefinitionUiTests
{
    private const string AppId = "app-a";
    private const string AgentName = "assistant";

    [Theory]
    [InlineData("v1", "v2", "Activate v2", "Deactivate v1")]
    [InlineData("v1", "v1", "Deactivate v1", "Activate v1")]
    [InlineData(null, "v2", "Activate v2", "Deactivate")]
    [InlineData("outside-history", "v2", "Activate v2", "Deactivate outside-history")]
    public async Task SelectedVersionActions(string? activeVersion, string selectedVersion, string expected, string absent)
    {
        // Selecting an inactive version must offer activation, not deactivation of another version.
        using var handler = new DefinitionHandler(activeVersion);
        var html = await RenderAsync<AgentDetails>(handler, new Dictionary<string, object?>
        {
            [nameof(AgentDetails.AppId)] = AppId,
            [nameof(AgentDetails.AgentName)] = AgentName,
        }, $"?version={selectedVersion}");

        Assert.Contains(expected, html, StringComparison.Ordinal);
        Assert.DoesNotContain(absent, html, StringComparison.Ordinal);
        if (activeVersion is not null)
            Assert.Contains($"Active {activeVersion}", html, StringComparison.Ordinal);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewAgentOffersPreparationTemplateWithoutMutations()
    {
        using var handler = new DefinitionHandler(null);
        var html = await RenderAsync<DefinitionEditor>(handler, new Dictionary<string, object?>
        {
            [nameof(DefinitionEditor.AppId)] = AppId,
        }, string.Empty);

        Assert.Contains("Use document-preparation (PdfReconciliationV1)", html, StringComparison.Ordinal);
        Assert.Contains("Use Responses API", html, StringComparison.Ordinal);
        Assert.Contains("Templates do not install themselves", html, StringComparison.Ordinal);
        Assert.Contains("Publish inactive version", html, StringComparison.Ordinal);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditorSuggestsVersionAndOffersOptInActivation()
    {
        using var handler = new DefinitionHandler("v1");
        var html = await RenderAsync<DefinitionEditor>(handler, new Dictionary<string, object?>
        {
            [nameof(DefinitionEditor.AppId)] = AppId,
            [nameof(DefinitionEditor.AgentName)] = AgentName,
        }, "?from=v1");

        Assert.Matches("value=\"[0-9]{4}-[0-9]{2}-[0-9]{2}\\.1\"", html);
        Assert.Contains("Make this definition active after publishing", html, StringComparison.Ordinal);
        Assert.Contains("Publish inactive version", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("<input[^>]*type=\"checkbox\"[^>]*checked", html);
        Assert.Contains("GET " + AdministrationRoutes.Format(AdministrationRoutes.ActiveDefinition, AppId, AgentName), handler.Requests);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuggestionChecksSnapshotsOutsideHistory()
    {
        using var handler = new DefinitionHandler("v1") { HiddenSuggestionCount = 2 };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var client = new AdministrationApiClient(httpClient, new AdministrationSessionState());

        var result = await client.GetSuggestedVersionAsync(AppId, AgentName, TestContext.Current.CancellationToken);

        Assert.Equal(ApiStatus.Success, result.Status);
        Assert.EndsWith(".3", result.Value, StringComparison.Ordinal);
        Assert.Equal(3, handler.SuggestionChecks);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET ", request, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ApiStatus.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiStatus.Unavailable)]
    public async Task SuggestionSurfacesLookupErrors(HttpStatusCode status, ApiStatus expected)
    {
        using var handler = new DefinitionHandler("v1") { SuggestionFailure = status };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var client = new AdministrationApiClient(httpClient, new AdministrationSessionState());

        var result = await client.GetSuggestedVersionAsync(AppId, AgentName, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(1, handler.SuggestionChecks);
    }

    private static async Task<string> RenderAsync<TComponent>(
        DefinitionHandler handler,
        Dictionary<string, object?> parameters,
        string query) where TComponent : IComponent
    {
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudServices();
        services.AddSupplyValueFromQueryProvider();
        services.AddSingleton<IJSRuntime, NoJavaScriptRuntime>();
        services.AddSingleton<NavigationManager>(new TestNavigationManager(query));
        services.AddSingleton(httpClient);
        services.AddSingleton<AdministrationSessionState>();
        services.AddSingleton<AdministrationApiClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
    }

    private sealed class DefinitionHandler(string? activeVersion) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public int HiddenSuggestionCount { get; init; }

        public HttpStatusCode? SuggestionFailure { get; init; }

        public int SuggestionChecks { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            var response = path switch
            {
                var value when value == AdministrationRoutes.Format(AdministrationRoutes.Providers, AppId) =>
                    Json(new ProviderCatalogueResponse
                    {
                        DefinitionTemplates = [CasCap.Constants.DocumentPreparationAgentTemplate.Create()],
                        Providers = [],
                        SupportedProviderTypes = ["Ollama"],
                        ReasoningEfforts = [],
                        AllowedProviderAuthorities = [],
                        AllowedMcpAuthorities = [],
                        AllowedServiceTools = [],
                        SupportedSchemaVersions = [1],
                        PublicationSupported = true,
                    }),
                var value when value == AdministrationRoutes.Format(AdministrationRoutes.Definitions, AppId, AgentName) =>
                    Json(new[]
                    {
                        new AgentDefinitionHistoryItemResponse { DefinitionVersion = "v2", IsActive = activeVersion == "v2" },
                        new AgentDefinitionHistoryItemResponse { DefinitionVersion = "v1", IsActive = activeVersion == "v1" },
                    }),
                var value when value == AdministrationRoutes.Format(AdministrationRoutes.ActiveDefinition, AppId, AgentName) =>
                    activeVersion is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(Snapshot(activeVersion)),
                var value when value == AdministrationRoutes.Format(AdministrationRoutes.DefinitionActivations, AppId, AgentName) =>
                    Json(Array.Empty<AgentDefinitionActivationResponse>()),
                var value when value.EndsWith("/v1", StringComparison.Ordinal) => Json(Snapshot("v1")),
                var value when value.EndsWith("/v2", StringComparison.Ordinal) => Json(Snapshot("v2")),
                _ => LookupSuggestion(),
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage LookupSuggestion()
        {
            SuggestionChecks++;
            if (SuggestionFailure is { } failure)
                return new HttpResponseMessage(failure);
            return SuggestionChecks <= HiddenSuggestionCount
                ? Json(Snapshot("hidden-version"))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static AgentDefinitionSnapshotResponse Snapshot(string version) => new()
        {
            AgentName = AgentName,
            DefinitionVersion = version,
            SchemaVersion = 1,
            Definition = AgentDefinitionTestData.CreatePublishRequest(version).Definition,
        };
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string query) => Initialize("https://example.com/", $"https://example.com/{query}");

        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class NoJavaScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException("Static component tests must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException("Static component tests must not invoke JavaScript.");
    }
}
