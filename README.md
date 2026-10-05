# agentizr

agentizr is a .NET 10 ASP.NET Core host for the multi-tenant CasCap agent runtime. The current first
slice establishes tenant-owned definitions, credentials, sessions, overrides, and execution
orchestration behind application-local contracts; it does not expose a runtime protocol yet.

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

`AgentRuntimeConfig` contains versioned provider and agent definitions keyed by tenant. The initial
`ConfiguredTenantContext` uses `DefaultTenantId`; authenticated request claims will replace that
bootstrap adapter before a public execution endpoint is added. Provider API keys remain separate
from returned `AgentDefinition` values and must arrive through a private configuration provider.

Set `AppConfig__OtlpExporterEndpoint` to an OTLP gRPC endpoint to enable OpenTelemetry export. When it is unset, the shared hosting library skips OpenTelemetry registration.

## Current Boundaries

The host now owns runtime contracts for definition and credential lookup, tenant-qualified session
and override state, and execution coordination around an `IAgentExecutor`. The following work remains:

- Authentication-derived tenant context and authorization
- Durable session, override, and definition stores
- Versioned protocol contracts and the client SDK
- Tenant tool allowlists, tool composition, and remote MCP connection ownership
- MCP or another agent transport
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
