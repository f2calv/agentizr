# CasCap.App.Server.Tests

Unit, in-memory protocol, PostgreSQL/Redis integration and opt-in live-runtime tests for
`CasCap.App.Server`. Most cases are credential-free; PostgreSQL and Redis cases require the Compose tests profile and the
live case can require deployment credentials.

## Purpose

Protocol tests launch the real application through `WebApplicationFactory<Program>` with scripted
execution or evaluation substitutes. Unit tests cover orchestration, authentication, metrics and
state; PostgreSQL integration cases exercise isolated per-fixture databases on reusable Compose services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 2 | 3 | Verifies `/` and `/healthz` return HTTP 200 and `/mcp` is absent by default |
| `AgentizrMcpEndpointTests` | 3 | 3 | Verifies MCP discovery of only the read-only status tool, its structured result, and `Origin` rejection |
| `AgentRuntimeProtocolTests` | 8 | 8 | Verifies typed-client execution, live event streaming, validation, session-control lifecycle, and effective instruction disclosure |
| `AgentDefinitionAdministrationProtocolTests` | 13 | 21 | Verifies inactive publication, evaluation, evaluation-gated activation, rollback, delegation-aware deactivation, audit attribution, strict policy/delegation/credential rejection, conflicts, and bounded history |
| `TenantSecurityStartupTests` | 7 | 7 | Verifies anonymous execution/control-plane rejection, separate execution/admin policies, required JWT/PostgreSQL and optional Redis in Production |
| `AgentExecutionCoordinatorTests` | 3 | 3 | Verifies definition, credential, override, session, bypass, and executor orchestration |
| `AgentRuntimeBuiltInToolsTests` | 3 | 5 | Verifies root runtime tools, reachable delegation metadata, tenant-local time formatting and opt-out |
| `AgentRuntimeMetricsTests` | 4 | 4 | Verifies outcome, usage, tool-error, reasoning-markup and unavailable-agent metric dimensions |
| `CommonAgentExecutorTests` | 7 | 7 | Verifies tool composition/discovery, unreachable-source fallback and per-round-trip history projection without binary content |
| `AgentDefinitionEvaluatorTests` | 4 | 6 | Verifies fixture-backed delegation grading, failed tool selection and answers, pass-rate thresholds, and request validation |
| `TenantStateIsolationTests` | 2 | 2 | Verifies process-local tenant isolation and collision-free keys |
| `AgentSessionLockTests` | 3 | 5 | Verifies fail-fast Development ownership, resource isolation, cancellation and idempotent release |
| `AgentSessionConflictTests` | 1 | 8 | Verifies all session mutation routes return HTTP 409 on overlap, including streaming before headers |
| `PostgresAgentStateTests` | 7 | 7 | Durable state after cache eviction/outage, tenant/version isolation, revision-qualified caching, database-only operation, connection-loss fencing and two-host contention |
| `AgentRunHistoryTests` | 4 | 7 | Durable messages/tools, redaction/binary exclusion, success/failure/cancellation, interruption recovery, opt-in and archive preservation after reset |
| `AuthenticatedTenantContextTests` | 4 | 4 | Verifies caller-derived tenant identity, missing/unmapped caller rejection, and opaque Redis keys |
| `AgentRuntimeAuthorizationTests` | 4 | 6 | Verifies delegated scopes, application roles, and caller-to-tenant mapping validation |
| `PostgresAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable publication, active-version changes, duplicate rejection, history, and tenant isolation |
| `PostgresAgentDefinitionStoreIntegrationTests` | 8 | 8 | Against real PostgreSQL: migrations, `jsonb` storage and round trip, sequential and concurrent duplicate-version conflicts, advisory-lock-serialised concurrent activation, the tenant-scoped activation lock, audited deactivation, and tenant isolation |
| `CachedAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable payload caching, pointer invalidation across activation/deactivation and absolute expiry before authoritative reload |
| `ConfigurationMcpCredentialStoreTests` | 2 | 2 | Verifies tenant-scoped Authorization resolution and shared Basic credential fallback |
| `ConfigurationProviderCredentialStoreTests` | 2 | 5 | Verifies the runtime's Entra credential is offered only to a tenant's opted-in providers |
| `SessionCompactionConfigTests` | 3 | 6 | Verifies per-provider compaction resolution (tenant override, runtime entry, defaults) and startup validation |
| `LiveAgentRuntimeSmokeTests` | 1 | 1 | Opt-in authenticated run, streaming, session inspection and cleanup against a complete live runtime |
| **Total** | **101** | **134** | Source-declared cases, not an execution result |

Counts expand every `[InlineData]` row; this project uses no `MemberData` or `ClassData` providers.

Reusable secret-free agent, provider, runtime-definition, and publication builders live in
`AgentDefinitionTestData`. Protocol and built-in tests consume these definition-contract fixtures so
test definitions do not drift into application-specific `AIConfig` setup.
`ScriptedEvaluationAgentFactory` replaces the model provider with a scripted chat client for
evaluation tests.

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 72 | ASP.NET Core host protocols, startup security, PostgreSQL persistence and Redis payload caching |
| `Agent Runtime` | 42 | Runtime orchestration, tools, metrics, evaluation, persistence, caching, locking and compaction |
| `Tenant Isolation` | 19 | Cross-tenant state, JWT permissions, caller mappings, MCP and provider credentials and identity isolation |
| `PostgreSQL` | 22 | Definitions, durable state and run history against the Compose tests-profile database |
| `Redis` | 7 | Revision-qualified state caching against the Compose tests-profile Redis service; also tagged `Integration` and `PostgreSQL` |
| `Local Agent Runtime` | 1 | Explicitly enabled smoke test against a complete running runtime |

## Skipped Tests

| Condition / source-declared reason | Cases affected |
| --- | ---: |
| Live runtime tests require `CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS=true` outside CI | 1 |
| Once enabled, missing/invalid live base address, agent name or requested certificate-auth settings | 1 (same case) |

These are conditional skips, not observed outcomes. PostgreSQL/Redis cases do not automatically
skip without their services: start the Compose tests profile or exclude both categories.

## Persistence Test Services

From the repository root:

```powershell
docker compose --profile tests up -d --wait postgres-tests redis-tests
dotnet test --project src\CasCap.App.Server.Tests --configuration Release --filter-trait 'Category=PostgreSQL'
docker compose --profile tests stop postgres-tests redis-tests
```

Fixtures create/drop only their own random test databases and use tenant-qualified Redis keys.
They never start ad hoc containers, touch the development database or flush Redis. See
[persistence setup](../../docs/persistence.md#local-development-and-tests) for ports, volumes and cleanup.

## Live Agent Runtime Smoke Test

The smoke test reaches a complete running runtime through `CasCap.AgentRuntime.Client`. It never runs
in CI and requires explicit local opt-in:

```powershell
$env:CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS = 'true'
$env:CASCAP_AGENTRUNTIME_LIVE_BASE_ADDRESS = 'https://runtime.example.com/'
$env:CASCAP_AGENTRUNTIME_LIVE_AGENT_NAME = '<tenant-agent-name>'
dotnet test --project src/CasCap.App.Server.Tests/CasCap.App.Server.Tests.csproj --filter-trait 'Category=Local Agent Runtime'
```

For certificate authentication, also set `CASCAP_AGENTRUNTIME_LIVE_SCOPE`,
`CASCAP_AGENTRUNTIME_LIVE_TENANT_ID`, `CASCAP_AGENTRUNTIME_LIVE_CLIENT_ID`, and
`CASCAP_AGENTRUNTIME_LIVE_CERTIFICATE_PATH`. The certificate path points to a local combined PEM file;
its content is read only at runtime and is never logged or persisted. Omit the authentication variables
only when testing a local runtime with authentication disabled.
`CASCAP_AGENTRUNTIME_LIVE_TIMEOUT_SECONDS` optionally replaces the default 300-second timeout.

## Layout

```text
Tests/
├── AgentDefinitionTestData.cs
├── ScriptedEvaluationAgentFactory.cs
├── Integration/
│   ├── AgentizrMcpEndpointTests.cs
│   ├── AgentizrWebApplicationFactory.cs
│   ├── AgentDefinitionAdministrationProtocolTests.cs
│   ├── AgentDefinitionAdministrationWebApplicationFactory.cs
│   ├── AgentRuntimeProtocolTests.cs
│   ├── AgentSessionConflictTests.cs
│   ├── EndpointSmokeTests.cs
│   ├── HostIntegrationCollection.cs
│   ├── McpAgentizrWebApplicationFactory.cs
│   ├── PostgreSqlFixture.cs
│   ├── PostgresAgentDefinitionStoreIntegrationTests.cs
│   ├── AgentRunHistoryTests.cs
│   ├── PostgresAgentStateTests.cs
│   ├── RedisStateFixture.cs
│   └── TenantSecurityStartupTests.cs
├── Local/
│   ├── LiveAgentRuntimeSmokeTests.cs
│   ├── LiveAgentRuntimeTestSettings.cs
│   ├── LocalAgentRuntimeFactAttribute.cs
│   └── LocalAgentRuntimeTestEnvironment.cs
└── Unit/
    ├── AgentDefinitionEvaluatorTests.cs
    ├── AgentRuntimeAuthorizationTests.cs
    ├── AgentRuntimeBuiltInToolsTests.cs
    ├── AgentRuntimeMetricsTests.cs
    ├── AuthenticatedTenantContextTests.cs
    ├── AgentExecutionCoordinatorTests.cs
    ├── AgentSessionLockTests.cs
    ├── CachedAgentDefinitionStoreTests.cs
    ├── ConfigurationMcpCredentialStoreTests.cs
    ├── ConfigurationProviderCredentialStoreTests.cs
    ├── CommonAgentExecutorTests.cs
    ├── InMemoryDistributedCache.cs
    ├── PostgresAgentDefinitionStoreTests.cs
    ├── SessionCompactionConfigTests.cs
    └── TenantStateIsolationTests.cs
```

## Run

Use the .NET 10 SDK and repository-selected Microsoft.Testing.Platform runner. Run from the
repository root only when you intend to execute tests. Debug references need adjacent
`CasCap.Common` and `CasCap.Api.Azure` checkouts; Release uses published dependencies.

Select the credential-free in-memory protocol category without PostgreSQL or local cases:

```powershell
dotnet test --project src\CasCap.App.Server.Tests\CasCap.App.Server.Tests.csproj --configuration Release --filter-trait 'Category=Integration' --filter-not-trait 'Category=PostgreSQL' --filter-not-trait 'Category=Redis'
```

For the full project, start the Compose test services first:

```powershell
dotnet test --project src\CasCap.App.Server.Tests\CasCap.App.Server.Tests.csproj --configuration Release
```

To run non-live coverage without Docker, append `--filter-not-trait 'Category=PostgreSQL' --filter-not-trait 'Category=Redis'` and
leave the local opt-in disabled. No tests were executed to produce this inventory.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.App.Server` | Application under test |
| `CasCap.AgentRuntime.Client` | Typed client exercised against the in-memory host |
| `CasCap.Common.Caching` | Redis contracts used by the production state implementations |
| `Microsoft.EntityFrameworkCore.Sqlite` | Credential-free relational definition-store tests |
| Root Compose `tests` profile | Reusable PostgreSQL/Redis services, with test-owned databases and key namespaces |
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |
