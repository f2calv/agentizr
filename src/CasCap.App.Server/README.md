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
stores with stateless delegated sessions. Every depth-zero run also receives runtime-owned
`get_current_datetime_state`, `get_agents`, and `get_providers` tools. Metadata covers only the root
agent's reachable active delegation graph; delegated agents do not receive another copy.

## Public Surface

| Endpoint | Response |
| --- | --- |
| `/` | Host identity |
| `/healthz` | ASP.NET Core health status |
| `/mcp` | Opt-in Streamable HTTP MCP endpoint (`CasCap:McpConfig:Enabled`, default off) serving the read-only `get_agentizr_status` tool: service name, version, commit and UTC time only. Requests carrying an `Origin` header are rejected with 403; when disabled the route does not exist |
| `POST /api/v1/agents/{agentName}/runs` | Executes one tenant-scoped agent turn |
| `POST /api/v1/agents/{agentName}/runs/stream` | Streams live execution events followed by the final response |
| `GET/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}` | Inspects or resets active session state |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/compact` | Compacts active session history |
| `PUT/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}` | Saves or deletes a named snapshot |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}/activate` | Loads a named snapshot |
| `GET/PUT /api/v1/agents/{agentName}/sessions/{sessionId}/overrides` | Gets or replaces complete runtime overrides |
| `GET /api/v1/agents/{agentName}/sessions/{sessionId}/instructions` | Gets the effective instructions (override, else active definition) when the tenant enables disclosure; otherwise 404 |
| `POST/GET /api/v1/agents/{agentName}/definitions` | Publishes an inactive immutable snapshot or lists history |
| `GET /api/v1/agents/{agentName}/definitions/{definitionVersion}` | Gets one immutable snapshot |
| `GET /api/v1/agents/{agentName}/definitions/active` | Gets the active snapshot |
| `POST /api/v1/agents/{agentName}/definitions/{definitionVersion}/evaluate` | Grades a version against fixture scenarios without activating it |
| `PUT /api/v1/agents/{agentName}/definitions/{definitionVersion}/activate` | Activates or rolls back to a version, optionally gated by an evaluation |
| `POST /api/v1/agents/{agentName}/definitions/active/deactivate` | Deactivates the active version with an audited reason; refused while another active agent delegates to it |
| `GET /api/v1/agents/{agentName}/definitions/activations` | Gets activation and rollback audit history |

Controllers are registered and mapped, but no controller exists in the initial scaffold.
The execution surface uses minimal APIs and derives tenancy from `ITenantContext`, never from the
request body.

## Configuration

`AppConfig` provides safe defaults for the telemetry meter prefix and service name. Set `AppConfig__OtlpExporterEndpoint` to enable OTLP export; leaving it unset disables the shared telemetry registration.

`AgentRuntimeConfig` binds from `CasCap:AgentRuntimeConfig`. Definitions are grouped under `Tenants`,
each with an explicit `DefinitionVersion`, provider dictionary, and agent dictionary. The tracked
configuration contains no tenant identifiers or credentials and is used only by the read-only
Development adapter. `TenantAgentConfig.TimeZoneId` configures the built-in tenant-local date/time
tool and defaults to UTC. `TenantAgentConfig.InstructionsDisclosureEnabled` (default `false`) lets
`agent.execute` callers read their own agents' effective instructions through the session
`instructions` route, so a chat host can show them without holding `agent.definition.read`.

Outside Development, `AgentRuntimeDatabaseConfig` supplies PostgreSQL. Immutable definition
snapshots store schema-versioned JSONB plus publisher/reason metadata, while a separate active
pointer selects one version per tenant agent. Publication never changes live traffic; activation
atomically advances the pointer and appends actor/reason audit history. Redis is a read-through
cache, not the authority.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation maps the configured caller claim (`azp` by default) through an explicit tenant/caller
allowlist to derive `ITenantContext.TenantId`. A caller identifier can belong to exactly one tenant.
Authentication and Redis state are mandatory outside Development; startup fails closed when either
is missing.

Execution and session operations require `agent.execute`. Definition administration uses separate
`agent.definition.read`, `agent.definition.publish` and `agent.definition.activate` permissions.
Permissions are accepted from delegated `scope`/`scp` claims or application `roles`. Publisher and
activation actor identifiers come from validated principal claims, never request bodies. Provider credentials remain behind
`IProviderCredentialStore` and definition publication rejects embedded API keys.

`AgentDefinitionPolicyConfig` limits publication to supported schema versions and explicit provider,
remote MCP and in-process service-tool allowlists. Version 1 requires inline instructions, rejects
arbitrary settings and prompt sources, uses strict JSON member handling, and rejects URI credentials,
queries and fragments. Activation verifies delegated agents exist, rejects cycles and caps delegation
depth; execution enforces the same cap. The delegation graph is revalidated under a tenant-scoped
PostgreSQL advisory lock, so concurrent activations of different agents cannot form a cycle.
`AgentRuntimeConfig.MaximumToolInvocationsPerRun` (default 50) bounds tool calls, including
delegations, across a run and all of its sub-agents; further calls return an error result so the
model answers with what it has. Tracked defaults permit only local Ollama and no MCP/service
tools.

Remote MCP definitions store only `ToolSource.Credential`, a logical tenant-local name. The runtime
resolves its `Scheme` and secret `Parameter` from `AgentRuntimeConfig:Tenants:{tenant}:McpCredentials`
through the final private configuration provider, validates availability before activation, and
passes the resulting Authorization header only to the per-run MCP transport.

Provider API keys resolve the same way from `AgentRuntimeConfig:Tenants:{tenant}:Providers:{name}:ApiKey`.
A tenant can instead list a provider name in `AgentRuntimeConfig:Tenants:{tenant}:EntraIdProviders`,
which authenticates that Azure OpenAI provider with the runtime's own Microsoft Entra identity (the
`AppConfig` certificate identity used for Key Vault). Use it for resources with key authentication
disabled, and grant the identity an OpenAI user role on the resource. No tenant inherits it implicitly.

### Definition Evaluation

An evaluation puts fixture-backed scenarios to one immutable version, inactive or not, through its
real provider and model-facing tool surface, then grades each run's answer and tool selection with
the `CasCap.Common.AI.Evaluation` grader. No external tool executes: read-only remote MCP, service
and delegation tools return the scenario's `ToolResponses` fixture (or a no-data response), and
actuating remote tools are recorded and sandboxed. Only the runtime's read-only built-ins run for
real. Delegation tools keep their production name and description but are never invoked, so each
definition is evaluated in isolation.

The evaluate endpoint requires `agent.definition.activate`. Activation accepts an optional
`Evaluation` and refuses the version, with a summary of the failed runs, when the pass rate falls
below `MinimumPassRate`. Set `AgentDefinitionPolicyConfig.RequireEvaluationBeforeActivation` to
refuse every activation, including rollback, that carries no evaluation.
`EvaluationRunTimeoutSeconds` bounds each run. Results are returned to the caller and not persisted.

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
| `CasCap.Common.AI` | Internal project: agent construction, providers, MCP tools, delegation and compaction |
| `CasCap.Common.Caching` | Redis-backed tenant session and override state |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the CasCap.Common and CasCap.Api.Azure dependencies from adjacent checkouts. Release builds use the centrally versioned NuGet packages.
