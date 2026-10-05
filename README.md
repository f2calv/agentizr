# agentizr

agentizr is a .NET 10 ASP.NET Core host for the multi-tenant CasCap agent runtime. The current first
slice establishes tenant-owned definitions, credentials, sessions, overrides, and execution
orchestration behind a versioned HTTP contract and typed client.

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
| `POST /api/v1/agents/{agentName}/runs` | Runs one tenant-scoped agent turn in Development |

The execution route is deliberately mapped only in Development until authentication derives a
trusted tenant identity. `RunAgentRequest` therefore contains no caller-supplied tenant identifier.

## NuGet Packages

| Package | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Versioned request and response DTOs |
| `CasCap.AgentRuntime.Client` | Typed HTTP client; authentication remains caller-owned |

## Configuration

Configuration follows the standard ASP.NET Core provider order. `AppConfig` supplies public-safe telemetry defaults in code, while `appsettings.json` configures Serilog and allowed hosts.

`AgentRuntimeConfig` contains versioned provider and agent definitions keyed by tenant. The initial
`ConfiguredTenantContext` uses `DefaultTenantId`; authenticated request claims will replace that
bootstrap adapter before the execution endpoint is enabled outside Development. Provider API keys remain separate
from returned `AgentDefinition` values and must arrive through a private configuration provider.

Outside Development, `TenantAuthenticationConfig` must enable JWT validation with a trusted
authority, audience, and tenant claim (default `tenant_id`). Redis must also be configured through
`CasCap:CachingConfig:RemoteCacheConnectionString`. Sessions and overrides use opaque
tenant/agent/session key digests and a configurable sliding expiry; definitions remain versioned
configuration and provider credentials remain in the final private configuration provider.

Development may omit JWT and Redis. It then uses the configured default tenant and process-local
state so the host and tests remain credential-free. Production startup fails when either control is
missing.

Set `AppConfig__OtlpExporterEndpoint` to an OTLP gRPC endpoint to enable OpenTelemetry export. When it is unset, the shared hosting library skips OpenTelemetry registration.

## Current Boundaries

The host now owns runtime contracts for definition and credential lookup, tenant-qualified session
and override state, and execution coordination around an `IAgentExecutor`. The following work remains:

- Durable dynamic definition storage and invalidation
- Host-specific execution enrichers after their measurements have a tenant-safe contract
- MCP or another agent transport
- Frontend assets
- Container, Helm, and deployment configuration

Version 1 execution accepts optional binary content and session bypass, and returns model identity,
finish reason, timing, token usage, tool-call names, attachments, and structured delegation and
compaction events. Signal delivery metadata, reactions, polls, spoken replies, and monitor-group
formatting remain Comms responsibilities.

Tools resolve from the current tenant's agent definition. In-process service tools are resolved from
the request scope, remote MCP clients are owned and disposed per run, and sub-agent tools recursively
resolve definitions and credentials through the same tenant-scoped stores.

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
