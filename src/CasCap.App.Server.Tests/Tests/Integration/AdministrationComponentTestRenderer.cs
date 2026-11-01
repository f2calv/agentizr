using CasCap.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace CasCap.Tests.Integration;

/// <summary>Renders administration components over synthetic HTTP without executing JavaScript or contacting providers.</summary>
internal static class AdministrationComponentTestRenderer
{
    /// <summary>Renders one component with route parameters and cascading query-string inputs.</summary>
    public static async Task<string> RenderAsync<TComponent>(
        HttpMessageHandler handler,
        Dictionary<string, object?> parameters,
        string query) where TComponent : IComponent
    {
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudServices();
        services.AddSupplyValueFromQueryProvider();
        services.AddSingleton<IJSRuntime, NoJavaScriptRuntime>();
        services.AddSingleton<NavigationManager>(new TestNavigationManager(query));
        services.AddSingleton(httpClient);
        services.AddSingleton<AdministrationSessionState>();
        services.AddSingleton<AdministrationApiClient>();
#if DEBUG
        services.AddSingleton<CasCap.Common.Configuration.Blazor.Services.RuntimeConfigurationClient<
            CasCap.AgentRuntime.Contracts.RuntimeConfiguration.RuntimeLoggingControls>>();
#endif
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
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
