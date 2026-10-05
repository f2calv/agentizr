# agentizr

agentizr is currently a vanilla .NET 10 ASP.NET Core host scaffold. It provides only the application bootstrap, observability wiring, root endpoint, controller registration, Problem Details, health checks, and a credential-free smoke test.

## Quick Start

The Debug solution resolves `CasCap.Common.Hosting.AspNetCore` from the adjacent `CasCap.Common` checkout:

```powershell
dotnet build agentizr.Debug.slnx --configuration Debug
dotnet run --project src/CasCap.App.Server/CasCap.App.Server.csproj
```

The running host exposes:

| Endpoint | Purpose |
| --- | --- |
| `/` | Identifies the host |
| `/healthz` | Reports host health |

## Configuration

Configuration follows the standard ASP.NET Core provider order. `AppConfig` supplies public-safe telemetry defaults in code, while `appsettings.json` configures Serilog and allowed hosts.

Set `AppConfig__OtlpExporterEndpoint` to an OTLP gRPC endpoint to enable OpenTelemetry export. When it is unset, the shared hosting library skips OpenTelemetry registration.

## Current Boundaries

This repository does not yet implement an agent domain or runtime protocol. The following work is deliberately deferred until its requirements and contracts are defined:

- Agent orchestration, sessions, tools, and protocol endpoints
- Authentication and authorization
- Database, cache, and durable state
- MCP or another agent transport
- Cloud-provider integrations
- Frontend assets
- Container, Helm, and deployment configuration

## Development

Build the local Debug graph:

```powershell
dotnet build agentizr.Debug.slnx --configuration Debug
```

Build the published-package Release graph:

```powershell
dotnet build agentizr.Release.slnx --configuration Release
```

Run the credential-free smoke tests:

```powershell
dotnet test --project src/CasCap.App.Server.Tests/CasCap.App.Server.Tests.csproj
```

## License

This project is released into the public domain under the [Unlicense](LICENSE).
