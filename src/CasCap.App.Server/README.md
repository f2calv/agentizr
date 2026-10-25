# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. It owns the multi-tenant execution and definition
control-plane APIs plus Development configuration adapters.

## Quick Start

Use the [source-run commands](../../README.md#run-from-source) with the Development example below,
or the [Helm chart](../../charts/agentizr/README.md) for Kubernetes. Development can run without
PostgreSQL, Redis or authentication, but still needs a reachable model provider for inference.
Tracked settings define no agents.

The runtime resolves a versioned definition and provider credential for the authenticated tenant,
then applies session overrides and coordinates execution and persistence. In non-Development
deployments, PostgreSQL owns definitions and runtime state across replicas; Redis is an optional cache.

The v1 execution response projects execution diagnostics into a stable wire surface: actual provider
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
| `/status` | Host identity |
| `/healthz` | ASP.NET Core health status |
| `/`, `/apps/...` | [Administration frontend](../../docs/administration-frontend.md) shell and WebAssembly assets, served when `AdministrationConfig` is enabled or in open Development; otherwise a setup page listing the missing settings |
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
| `GET /admin/api/session` | Describes the administrator session and issues an antiforgery token |
| `GET /admin/login`, `POST /admin/logout` | Browser navigations that start OpenID Connect sign-in or end the session |
| `GET /admin/api/apps` | Lists the apps the administrator may manage, with caller applications and agent counts |
| `GET /admin/api/apps/{appId}/agents` | Pages the app's agents, including inactive and never-activated agents |
| `GET /admin/api/apps/{appId}/providers` | Gets secret-free provider registrations and the definition policy |
| `/admin/api/apps/{appId}/agents/{agentName}/definitions/...` | Mirrors the definition routes above for the selected app; activation and deactivation require the administrator's observed active version and return 409 when it changed |

The application API uses thin
[`AgentRuntimeController`](Controllers/AgentRuntimeController.cs) and
[`AgentDefinitionsController`](Controllers/AgentDefinitionsController.cs) actions. Each delegates to
one typed method on [`AgentRuntimeApiService`](Services/AgentRuntimeApiService.cs) or
[`AgentDefinitionApiService`](Services/AgentDefinitionApiService.cs), which owns DTO projection
and streaming orchestration. Services return DTOs or typed outcomes; controllers select the HTTP
response with `TypedResults`. Concrete return types provide response metadata without duplicate
success/error attributes. Explicit metadata covers controller validation, session-conflict handling
and dynamic status codes.

The administration API serves the browser frontend from
[`AdministrationController`](Controllers/AdministrationController.cs) and
[`AdministrationDefinitionsController`](Controllers/AdministrationDefinitionsController.cs) through
[`AdministrationApiService`](Services/AdministrationApiService.cs). Its route and DTO contracts live in
the [browser client project](../CasCap.App.Client/README.md), not the published contracts package.
Every action requires the app-administrator policy: the route's `appId` is only a requested target
until [`AdministrationAccessService`](Services/AdministrationAccessService.cs) confirms the user is
assigned to it, after which the app becomes the tenant for that request. Failed authorization returns
401 or 403 Problem Details rather than a redirect.

Routes remain defined by the version 1 contract, and endpoint names are stable. Controller validation
checks request DTOs before execution. The streaming action returns the existing JSON-array stream
without buffering it into a collection. The host retains its small status, health and MCP mappings.

API services and authorization policies are registered by
[`AddAgentRuntimeApi`](Extensions/ServiceCollectionExtensions.cs). Execution, definition reads,
publication and activation retain separate permissions. Only Development with authentication
explicitly disabled permits anonymous controller access; non-Development startup still fails closed.
Administration endpoints are excluded from that anonymous convention and are open only when
`AdministrationConfig` is also disabled.

The runtime derives tenancy from
[`ITenantContext`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.App.Server/Abstractions/ITenantContext.cs), never from the
request body. `/healthz` uses the host's health checks; it does not probe the model provider,
PostgreSQL or Redis.

## Configuration

The opt-in [runtime logging soak](../../docs/runtime-configuration-soak.md) uses typed PostgreSQL
configuration and the standard options monitor without restarting the host. It is currently limited
to Debug/local-source builds; normal Release builds remain unchanged and reject attempts to enable
the unpublished feature. The root deployment script already selects Debug and its sibling project
references. No configuration store is contacted unless the soak is explicitly enabled.

`AppConfig` provides safe defaults for the telemetry meter prefix and service name. Set `AppConfig__OtlpExporterEndpoint` to enable OTLP export; leaving it unset disables the shared telemetry registration.

`AgentRuntimeConfig` binds from `CasCap:AgentRuntimeConfig`. Definitions are grouped under `Tenants`,
each with an explicit `DefinitionVersion`, provider dictionary, and agent dictionary. The tracked
configuration contains no tenant identifiers or credentials and is used only by the read-only
Development adapter. `TenantAgentConfig.TimeZoneId` configures the built-in tenant-local date/time
tool and defaults to UTC. `TenantAgentConfig.CurrentTimeContextEnabled` (default `true`) adds a
line such as `[Current local time: Monday 2026-10-19 06:15 (UTC, UTC+00:00)]` to the user
message of every run, including delegated runs, so agents know the date without a tool call. It
travels with the message rather than the instructions, which keeps instructions identical between
runs for provider prompt caching and dates each stored turn.
`TenantAgentConfig.InstructionsDisclosureEnabled` (default `false`) lets
`agent.execute` callers read their own agents' effective instructions through the session
`instructions` route, so a chat host can show them without holding `agent.definition.read`.

Outside Development, `DatabaseConfig` (`CasCap:DatabaseConfig`) supplies PostgreSQL. `DatabaseName`
optionally replaces the database in `ConnectionString`, so applications sharing one server
connection string each use their own database. Immutable definition
snapshots store schema-versioned JSONB plus publisher/reason metadata, while a separate active
pointer selects one version per tenant agent. Publication never changes live traffic; activation
atomically advances the pointer and appends actor/reason audit history. Redis is a read-through
cache, not the authority.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation maps the configured caller claim (`azp` by default) through an explicit tenant/caller
allowlist to derive `ITenantContext.TenantId`. A caller identifier can belong to exactly one tenant.
Authentication and PostgreSQL are mandatory outside Development; startup fails closed when either
is missing.

Execution and session operations require `agent.execute`. Definition administration uses separate
`agent.definition.read`, `agent.definition.publish` and `agent.definition.activate` permissions.
Permissions are accepted from delegated `scope`/`scp` claims or application `roles`. Publisher and
activation actor identifiers come from validated principal claims, never request bodies. Provider credentials remain behind
[`IProviderCredentialStore`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.App.Server/Abstractions/IProviderCredentialStore.cs)
and definition publication rejects embedded API keys.

`AdministrationConfig` binds from `CasCap:AdministrationConfig` and controls the browser frontend.
`AppAdministrators` maps each app to the user identifiers, read from `AdministratorClaimType`
(`oid` by default), who may read, publish and activate its definitions. An app is the tenant
boundary shared by `TenantAuthenticationConfig.TenantCallers` and `AgentRuntimeConfig.Tenants`.
Assignments are configuration-only; machine callers and their scopes never grant administration.

When enabled, administrators sign in with an OpenID Connect authorization-code flow with PKCE
against a dedicated web registration (`Authority`, `ClientId` and a secret-supplied `ClientSecret`),
and the host issues its own Secure, HttpOnly `agentizr-admin` cookie. Identity tokens are not saved
and never reach the browser. Register the exact HTTPS `CallbackPath` (`/signin-oidc`) and
`SignedOutCallbackPath` (`/signout-callback-oidc`) redirect URIs. Behind a TLS-terminating proxy, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the callbacks keep their HTTPS scheme. Administration
mutations and sign-out require the antiforgery token from `/admin/api/session`, sent in the
`X-XSRF-TOKEN` header or the sign-out form. With PostgreSQL configured, the Data Protection key ring
that protects cookies and antiforgery tokens is shared through the `data_protection_keys` table, so
any replica accepts them; the keys are not yet encrypted at rest.

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

`AgentRuntimeConfig.SessionCompaction` sets the session compaction policy per provider name for
every tenant, and `TenantAgentConfig.SessionCompaction` replaces an entry for one tenant. Each
`SessionCompactionConfig` strips tool calls and results on every run, trims the history to
`TargetMessageCount` non-system messages once it exceeds `TriggerMessageCount` (both default 20),
and reports `session.compacted` events according to `Notification`: `Off`, `WindowTrimmed`
(default) or `Always`. Startup fails when a trigger is below its target. See
[Session Compaction](../../docs/session-compaction.md) for the measurements behind the settings.

Remote MCP definitions store only `ToolSource.Credential`, a logical tenant-local name. The runtime
resolves its `Scheme` and secret `Parameter` from `AgentRuntimeConfig:Tenants:{tenant}:McpCredentials`
through the final private configuration provider, validates availability before activation, and
passes the resulting Authorization header only to the per-run MCP transport. A `Basic` credential
without a `Parameter` is built from the runtime's shared `CasCap:ApiAuthConfig` username and
password.

Provider API keys resolve the same way from `AgentRuntimeConfig:Tenants:{tenant}:Providers:{name}:ApiKey`.
A tenant can instead list a provider name in `AgentRuntimeConfig:Tenants:{tenant}:EntraIdProviders`,
which authenticates that Azure OpenAI provider with the runtime's own Microsoft Entra identity (the
`AzureAuthConfig` identity used for Key Vault). Use it for resources with key authentication
disabled, and grant the identity an OpenAI user role on the resource. No tenant inherits it implicitly.

### Definition Evaluation

An evaluation puts fixture-backed scenarios to one immutable version, inactive or not, through its
real provider and model-facing tool surface, then grades each run's answer and tool selection with
the `CasCap.AgentRuntime.Evaluation` grader. No external tool executes: read-only remote MCP, service
and delegation tools return the scenario's `ToolResponses` fixture (or a no-data response), and
actuating remote tools are recorded and sandboxed. Only the runtime's read-only built-ins run for
real. Delegation tools keep their production name and description but are never invoked, so each
definition is evaluated in isolation.

The evaluate endpoint requires `agent.definition.activate`. Activation accepts an optional
`Evaluation` and refuses the version, with a summary of the failed runs, when the pass rate falls
below `MinimumPassRate`. Set `AgentDefinitionPolicyConfig.RequireEvaluationBeforeActivation` to
refuse every activation, including rollback, that carries no evaluation.
`EvaluationRunTimeoutSeconds` bounds each run. Results are returned to the caller and not persisted.

Sessions, snapshots and overrides use PostgreSQL storage behind the existing store abstractions,
with a durable revision check before reading their Redis-cached payloads. Runtime mutations hold a
connection-bound PostgreSQL session advisory lock; overlapping requests return HTTP 409.
`AgentRuntimeConfig.StateSlidingExpirationHours` controls only Redis payload TTL, not durable
retention. Development without PostgreSQL uses process-local stores.

The definition version participates in every session and override key, so changing the active
version invalidates state by namespace without scanning Redis. Previous snapshots and state remain
available for rollback until explicitly removed.

Immutable definition contents are cached with `DefinitionCacheExpirationMinutes` (default `60`,
sliding expiry). `ActiveDefinitionCacheSeconds` bounds active-pointer cache age (default `5`,
absolute expiry; `0` disables it), with invalidation after activation/deactivation. An already-started
run keeps its selected immutable version. Session ownership/state still requires PostgreSQL.

`ConversationHistoryEnabled` (default `false`) opts into durable debugging history: user/model text,
tool calls/results, partial failed activity and turn outcomes. Reset/compaction leaves that archive
intact. See [persistence](../../docs/persistence.md) for privacy, tables and Compose test setup, and
[high availability](../../docs/high-availability.md) for concurrency and cutover constraints.

Runtime turns and session mutations acquire exclusive tenant/agent/session ownership across
definition versions. Overlap returns HTTP 409 without waiting. State writes use the owning
PostgreSQL connection and cannot reconnect after ownership is lost. Development without PostgreSQL
uses process-local coordination. See [high availability](../../docs/high-availability.md) for rollout
and failure constraints.

Named session snapshots use their own opaque state namespace. Session inspection and compaction are
performed inside the runtime through Agent Framework serialization; raw session JSON never crosses
the HTTP boundary.

EF migrations are applied externally and create schema only. The model deliberately contains no
`HasData` payload.

## Configuration Examples

For Development, add this synthetic bootstrap to the gitignored `appsettings.Local.Development.json`
at the repository root. The example assumes a local Ollama server with `example-model` already
loaded; replace the model name with one installed on your server.

```json
{
  "CasCap": {
    "AgentRuntimeConfig": {
      "DefaultTenantId": "example-tenant",
      "Tenants": {
        "example-tenant": {
          "Providers": {
            "Local": {
              "Type": "Ollama",
              "Endpoint": "http://localhost:11434",
              "ModelName": "example-model"
            }
          },
          "Agents": {
            "assistant": {
              "Provider": "Local",
              "Name": "assistant",
              "Description": "Example assistant",
              "Prompt": "Respond to the user's request.",
              "Instructions": "Give concise, accurate answers."
            }
          }
        }
      }
    }
  }
}
```

This uses the default definition version `1` and process-local state. It supports execution and
definition reads; publication, activation and deactivation require PostgreSQL and return 501 with
the read-only configuration adapter.

For an authenticated deployment, the following non-secret settings illustrate caller mapping,
publication policy and state tuning. They supplement, not replace, secret-supplied connections
and credentials:

```json
{
  "CasCap": {
    "TenantAuthenticationConfig": {
      "Enabled": true,
      "Authority": "https://identity.example.com/",
      "Audience": "api://example-runtime",
      "TenantCallers": {
        "example-tenant": ["00000000-0000-0000-0000-000000000000"]
      }
    },
    "AdministrationConfig": {
      "Enabled": true,
      "Authority": "https://identity.example.com/",
      "ClientId": "00000000-0000-0000-0000-000000000000",
      "AppAdministrators": {
        "example-tenant": ["00000000-0000-0000-0000-000000000000"]
      }
    },
    "AgentDefinitionPolicyConfig": {
      "AllowedProviderAuthorities": ["https://llm.example.com"],
      "AllowedMcpAuthorities": ["https://tools.example.com"],
      "AllowedServiceTools": [],
      "RequireEvaluationBeforeActivation": true,
      "EvaluationRunTimeoutSeconds": 120
    },
    "AgentRuntimeConfig": {
      "StateSlidingExpirationHours": 168,
      "DefinitionCacheExpirationMinutes": 60,
      "ActiveDefinitionCacheSeconds": 5,
      "ConversationHistoryEnabled": false,
      "MaximumToolInvocationsPerRun": 50,
      "SessionCompaction": {
        "ExampleProvider": {
          "TargetMessageCount": 20,
          "TriggerMessageCount": 30,
          "Notification": "WindowTrimmed"
        }
      },
      "Tenants": {
        "example-tenant": {
          "TimeZoneId": "UTC",
          "CurrentTimeContextEnabled": true,
          "InstructionsDisclosureEnabled": false
        }
      }
    }
  }
}
```

Supply `CasCap__DatabaseConfig__ConnectionString` and
`CasCap__CachingConfig__RemoteCacheConnectionString` through a private provider or secret-backed
environment variables. Redis requires JSON serialization (`CasCap:CachingConfig:RemoteCache:SerializationType`,
already set to `Json` in tracked settings). Provider API keys and logical MCP credentials use the
tenant paths described above and must stay out of definition JSON and tracked settings.

Apply database migrations separately before serving traffic. From a configured Release build:

```powershell
dotnet run --project src\CasCap.App.Server\CasCap.App.Server.csproj --configuration Release --no-launch-profile -- --migrate
```

The migration process still passes authentication/configuration startup validation outside
Development and needs PostgreSQL, but does not require Redis or start the HTTP server. Normal
startup never migrates the database.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |
| `CasCap.AgentRuntime.Execution` | Internal project: agent construction, providers, MCP tools, delegation and compaction |
| `CasCap.AgentRuntime.Evaluation` | Internal project: definition evaluation grading, fixtures and recording |
| `CasCap.Common.Caching` | Optional Redis payload/definition caching |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the CasCap.Common and CasCap.Api.Azure dependencies from adjacent checkouts. Release builds use the centrally versioned NuGet packages.
