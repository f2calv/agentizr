# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. It owns the initial multi-tenant runtime contracts and
configuration adapters and exposes the v1 execution contract in Development.

## Purpose

`Program` delegates startup to the partial `AppHost`, whose files separate application services, web API registration, and endpoint mapping. The host initializes the shared Serilog and OpenTelemetry pipeline before building the application.

The runtime service layer resolves a versioned `AgentDefinition` and provider credential for the
current `ITenantContext`, then coordinates tenant-qualified session and override state around an
`IAgentExecutor`. Development may use process-local stores; non-Development hosts require Redis-backed
state so sessions and overrides survive replica and pod replacement.

The v1 execution response projects Common.AI diagnostics into a stable wire surface: provider model,
finish reason, timing, token usage, tool-call names, tool-produced attachments, and structured
delegation/compaction events. Tool arguments are not exposed.

Tool composition is tenant-scoped: service tools resolve from request DI, remote MCP connections are
owned for one execution, and sub-agent tools recursively use the same definition and credential
stores with stateless delegated sessions.

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
configuration contains no tenant identifiers or credentials and is used only by the read-only
Development adapter.

Outside Development, `AgentRuntimeDatabaseConfig` supplies PostgreSQL. Immutable definition
snapshots store schema-versioned JSONB plus publisher/reason metadata, while a separate active
pointer selects one version per tenant agent. Redis is a read-through cache, not the authority.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation derives `ITenantContext.TenantId` from the configured claim. Authentication and Redis
state are mandatory outside Development; startup fails closed when either is missing.

Sessions and overrides use `CasCap.Common.Caching` Redis storage with opaque SHA-256 keys and
`AgentRuntimeConfig.StateSlidingExpirationHours`. Redis stores serialized runtime state and
secret-free active-definition cache entries, never provider credentials. Development falls back to
process-local stores.

The definition version participates in every session and override key, so changing the active
version invalidates state by namespace without scanning Redis. Previous snapshots and state remain
available for rollback until normal retention expires.

EF migrations are applied externally and create schema only. Definitions are published through
`IAgentDefinitionStore`; the model deliberately contains no `HasData` payload.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |
| `CasCap.Common.AI` | Agent and provider definition types used by the initial configuration adapter |
| `CasCap.Common.Caching` | Redis-backed tenant session and override state |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the dependency from the adjacent `CasCap.Common` checkout. Release builds use the centrally versioned NuGet package.
