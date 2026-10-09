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

The running host exposes versioned execution, session-control and definition-management APIs,
including:

| Endpoint | Purpose |
| --- | --- |
| `/` | Identifies the host |
| `/healthz` | Reports host health |
| `POST /api/v1/agents/{agentName}/runs` | Runs one tenant-scoped agent turn |
| `POST /api/v1/agents/{agentName}/definitions` | Publishes an inactive immutable definition |
| `PUT /api/v1/agents/{agentName}/definitions/{version}/activate` | Activates or rolls back a definition version |

Outside Development, JWT authentication derives trusted tenant and control-plane actor identity.
Request DTOs contain neither tenant nor actor identifiers.

### Local AI Provider Profiles

Docker Compose runs one Agent Runtime with multiple configuration-backed providers and
provider-specific agent definitions. Profiles choose which local provider containers start:

```powershell
# llama.cpp model router: Qwen3.5 4B Q5 text plus 0.8B vision on demand
docker compose --profile llama-cpp up

# Ollama: Qwen3.5 4B
docker compose --profile ollama up

# Both providers (requires enough GPU capacity for both servers)
docker compose --profile all up
```

The single Agent Runtime is exposed at <http://localhost:5090>. Run
`llamacpp` or `ollama` to select a provider through the existing agent route.
llama.cpp is exposed at <http://localhost:11434>; Ollama uses <http://localhost:11435>. On small
GPUs, run only one provider profile at a time. Debug project references require adjacent
`CasCap.Common` and `CasCap.Api.Azure` checkouts; Compose mounts them read-only and builds in its
container-local workspace. Model and NuGet caches use named volumes.

Agent definitions currently select one provider. Runtime overrides may change model and instructions,
but the run API does not accept an arbitrary provider selector. Switching providers for one logical
agent therefore means invoking a provider-specific agent name or publishing and activating a new
definition version. Azure OpenAI is supported by the runtime; Azure AI Foundry remains unsupported.

## NuGet Packages

| Package | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Versioned request and response DTOs |
| `CasCap.AgentRuntime.Client` | Typed HTTP client; authentication remains caller-owned |

## Configuration

Configuration follows the standard ASP.NET Core provider order. `AppConfig` supplies public-safe telemetry defaults in code, while `appsettings.json` configures Serilog and allowed hosts.

`AgentRuntimeConfig` provides a read-only Development bootstrap adapter. Outside Development,
PostgreSQL owns immutable schema-versioned JSONB definition snapshots and an active-version pointer;
Redis caches active lookups. Provider API keys remain separate from stored `AgentDefinition` values
and must arrive through a private configuration provider.

Outside Development, `TenantAuthenticationConfig` must enable JWT validation with a trusted
authority and audience. Validated caller application identifiers (the `azp` claim by default) map
to logical tenants through an explicit allowlist. Execution requires the `agent.execute` permission;
definition administration uses separate read, publish and activate permissions. Permissions may
arrive through delegated `scope`/`scp` claims or application `roles`. Redis must also be configured
through `CasCap:CachingConfig:RemoteCacheConnectionString`. Sessions and overrides use opaque
tenant/agent/session key digests and a configurable sliding expiry. Provider and logical MCP
credentials remain in the final private configuration provider.

Development bootstrap definitions and credentials use immutable startup options. Every state key
includes the definition version, so publishing a new PostgreSQL version selects a fresh namespace
immediately while the previous version remains available for rollback until its TTL expires.

Definition publication appends an inactive immutable snapshot. Activation alone advances the active
pointer and appends actor/reason audit history; this is intentionally not replay-based event
sourcing. No definitions are seeded through EF `HasData`.

Remote MCP tool definitions carry only a logical credential name. The runtime resolves its
tenant-scoped Authorization header from private configuration and creates/disposes the authenticated
MCP client per run.

Development may omit JWT and Redis. It then uses the configured default tenant and process-local
state so the host and tests remain credential-free. Production startup fails when either control is
missing.

Set `AppConfig__OtlpExporterEndpoint` to an OTLP gRPC endpoint to enable OpenTelemetry export. When it is unset, the shared hosting library skips OpenTelemetry registration.

## Current Boundaries

The host owns runtime contracts for definitions, credentials, tenant-qualified state and execution.
The following deployment work remains private-environment-specific:

- Homelab Argo CD Application values, database/Redis Secrets and initial definitions
- Workload identity/JWT acquisition for non-Development callers
- Frontend assets

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

Build the Debug image with adjacent project references:

```powershell
./build.ps1
```

Copy `deploy.local.psd1.example` to the gitignored `deploy.local.psd1`, set the private GitOps
repository and Application path, then publish the Debug image and chart and patch that Application:

```powershell
./deploy.ps1 -Chart
```

The chart's `migrate` alias runs `agentizr --migrate` as an external PreSync Job. The runtime never
applies migrations during normal startup.

## License

This project is released into the public domain under the [Unlicense](LICENSE).
