# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. It owns the multi-tenant execution and definition
control-plane APIs plus Development configuration adapters.

## Purpose

`Program` delegates startup to the partial `AppHost`, whose files separate application services, web API registration, and endpoint mapping. The host initializes the shared Serilog and OpenTelemetry pipeline before building the application.

The runtime service layer resolves a versioned `AgentDefinition` and provider credential for the
current `ITenantContext`, then coordinates tenant-qualified session and override state around an
`IAgentExecutor`. Development may use process-local stores; non-Development hosts require Redis-backed
state so sessions and overrides survive replica and pod replacement.

The v1 execution response projects Common.AI diagnostics into a stable wire surface: actual provider
model, finish reason, timing, token usage, tool calls, session summary, tool-produced attachments,
and structured delegation/compaction events. Tool arguments and property bags require explicit
diagnostic disclosure on the authenticated request.

Tool composition is tenant-scoped: service tools resolve from request DI, remote MCP connections are
owned for one execution, and sub-agent tools recursively use the same definition and credential
stores with stateless delegated sessions.

## Public Surface

| Endpoint | Response |
| --- | --- |
| `/` | Host identity |
| `/healthz` | ASP.NET Core health status |
| `POST /api/v1/agents/{agentName}/runs` | Executes one tenant-scoped agent turn |
| `POST /api/v1/agents/{agentName}/runs/stream` | Streams live execution events followed by the final response |
| `GET/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}` | Inspects or resets active session state |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/compact` | Compacts active session history |
| `PUT/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}` | Saves or deletes a named snapshot |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}/activate` | Loads a named snapshot |
| `GET/PUT /api/v1/agents/{agentName}/sessions/{sessionId}/overrides` | Gets or replaces complete runtime overrides |
| `POST/GET /api/v1/agents/{agentName}/definitions` | Publishes an inactive immutable snapshot or lists history |
| `GET /api/v1/agents/{agentName}/definitions/{definitionVersion}` | Gets one immutable snapshot |
| `GET /api/v1/agents/{agentName}/definitions/active` | Gets the active snapshot |
| `PUT /api/v1/agents/{agentName}/definitions/{definitionVersion}/activate` | Activates or rolls back to a version |
| `GET /api/v1/agents/{agentName}/definitions/activations` | Gets activation and rollback audit history |

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
pointer selects one version per tenant agent. Publication never changes live traffic; activation
atomically advances the pointer and appends actor/reason audit history. Redis is a read-through
cache, not the authority.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation derives `ITenantContext.TenantId` from the configured claim. Authentication and Redis
state are mandatory outside Development; startup fails closed when either is missing.

Definition administration uses separate `agent.definition.read`, `agent.definition.publish` and
`agent.definition.activate` scopes. Publisher and activation actor identifiers come from validated
principal claims, never request bodies. Provider credentials remain behind
`IProviderCredentialStore` and definition publication rejects embedded API keys.

`AgentDefinitionPolicyConfig` limits publication to supported schema versions and explicit provider,
remote MCP and in-process service-tool allowlists. Version 1 requires inline instructions, rejects
arbitrary settings and prompt sources, uses strict JSON member handling, and rejects URI credentials,
queries and fragments. Activation verifies delegated agents exist, rejects cycles and caps delegation
depth; execution enforces the same cap. Tracked defaults permit only local Ollama and no MCP/service
tools.

Remote MCP definitions store only `ToolSource.Credential`, a logical tenant-local name. The runtime
resolves its `Scheme` and secret `Parameter` from `AgentRuntimeConfig:Tenants:{tenant}:McpCredentials`
through the final private configuration provider, validates availability before activation, and
passes the resulting Authorization header only to the per-run MCP transport.

Sessions and overrides use `CasCap.Common.Caching` Redis storage with opaque SHA-256 keys and
`AgentRuntimeConfig.StateSlidingExpirationHours`. Redis stores serialized runtime state and
secret-free active-definition cache entries, never provider credentials. Development falls back to
process-local stores.

The definition version participates in every session and override key, so changing the active
version invalidates state by namespace without scanning Redis. Previous snapshots and state remain
available for rollback until normal retention expires.

Execution reads the active version pointer from PostgreSQL and caches immutable definitions under
version-qualified keys, so activation cannot race with stale active-definition cache writes.

Named session snapshots use their own opaque state namespace. Session inspection and compaction are
performed inside the runtime through Agent Framework serialization; raw session JSON never crosses
the HTTP boundary.

EF migrations are applied externally and create schema only. The model deliberately contains no
`HasData` payload.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |
| `CasCap.Common.AI` | Agent and provider definition types used by the initial configuration adapter |
| `CasCap.Common.Caching` | Redis-backed tenant session and override state |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the dependency from the adjacent `CasCap.Common` checkout. Release builds use the centrally versioned NuGet package.
