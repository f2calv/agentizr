# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. This project currently provides infrastructure wiring only; it contains no agent domain or protocol implementation.

## Purpose

`Program` delegates startup to the partial `AppHost`, whose files separate application services, web API registration, and endpoint mapping. The host initializes the shared Serilog and OpenTelemetry pipeline before building the application.

## Public Surface

| Endpoint | Response |
| --- | --- |
| `/` | Host identity |
| `/healthz` | ASP.NET Core health status |

Controllers are registered and mapped, but no controller exists in the initial scaffold.

## Configuration

`AppConfig` provides safe defaults for the telemetry meter prefix and service name. Set `AppConfig__OtlpExporterEndpoint` to enable OTLP export; leaving it unset disables the shared telemetry registration.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |

Debug builds resolve the dependency from the adjacent `CasCap.Common` checkout. Release builds use the centrally versioned NuGet package.
