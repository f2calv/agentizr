# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. It owns the initial multi-tenant runtime contracts and
configuration adapters and exposes the v1 execution contract in Development.

## Purpose

`Program` delegates startup to the partial `AppHost`, whose files separate application services, web API registration, and endpoint mapping. The host initializes the shared Serilog and OpenTelemetry pipeline before building the application.

The runtime service layer resolves a versioned `AgentDefinition` and provider credential for the
current `ITenantContext`, then coordinates tenant-qualified session and override state around an
`IAgentExecutor`. The initial stores are process-local and exist to prove isolation and orchestration;
durable implementations replace them before horizontal scaling.

## Public Surface

| Endpoint | Response |
| --- | --- |
| `/` | Host identity |
| `/healthz` | ASP.NET Core health status |
| `POST /api/v1/agents/{agentName}/runs` | Version 1 agent execution; Development only until tenant authentication exists |

Controllers are registered and mapped, but no controller exists in the initial scaffold.
The execution surface uses minimal APIs and derives tenancy from `ITenantContext`, never from the
request body.

## Configuration

`AppConfig` provides safe defaults for the telemetry meter prefix and service name. Set `AppConfig__OtlpExporterEndpoint` to enable OTLP export; leaving it unset disables the shared telemetry registration.

`AgentRuntimeConfig` binds from `CasCap:AgentRuntimeConfig`. Definitions are grouped under `Tenants`,
each with an explicit `DefinitionVersion`, provider dictionary, and agent dictionary. The tracked
configuration contains no tenant identifiers or credentials.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation derives `ITenantContext.TenantId` from the configured claim. Authentication and Redis
state are mandatory outside Development; startup fails closed when either is missing.

Sessions and overrides use `CasCap.Common.Caching` Redis storage with opaque SHA-256 keys and
`AgentRuntimeConfig.StateSlidingExpirationHours`. Redis stores only serialized runtime state, not
definitions or provider credentials. Development falls back to process-local stores.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |
| `CasCap.Common.AI` | Agent and provider definition types used by the initial configuration adapter |
| `CasCap.Common.Caching` | Redis-backed tenant session and override state |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the dependency from the adjacent `CasCap.Common` checkout. Release builds use the centrally versioned NuGet package.
