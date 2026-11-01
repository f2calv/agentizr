using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddScoped<AdministrationSessionState>();
builder.Services.AddHttpClient<AdministrationApiClient>(client =>
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
#if DEBUG
builder.Services.AddHttpClient<CasCap.Common.Configuration.Blazor.Services.RuntimeConfigurationClient<
    CasCap.AgentRuntime.Contracts.RuntimeConfiguration.RuntimeLoggingControls>>(client =>
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
#endif

await builder.Build().RunAsync();
