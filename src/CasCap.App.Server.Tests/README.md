# CasCap.App.Server.Tests

Credential-free unit and integration tests for the `CasCap.App.Server` host and runtime ownership boundary.

## Purpose

The tests launch the real application through `WebApplicationFactory<Program>` and verify that its public baseline endpoints respond successfully without credentials or external services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 1 | 2 | Verifies `/` and `/healthz` return HTTP 200 |
| `AgentRuntimeProtocolTests` | 6 | 6 | Verifies typed-client execution, live event streaming, validation, and complete session-control lifecycle |
| `AgentDefinitionAdministrationProtocolTests` | 13 | 21 | Verifies inactive publication, evaluation, evaluation-gated activation, rollback, delegation-aware deactivation, audit attribution, strict policy/delegation/credential rejection, conflicts, and bounded history |
| `TenantSecurityStartupTests` | 6 | 6 | Verifies anonymous execution/control-plane rejection, separate execution/admin policies, and Production requirements for authentication, tenant mappings, and Redis |
| `AgentExecutionCoordinatorTests` | 3 | 3 | Verifies definition, credential, override, session, bypass, and executor orchestration |
| `AgentRuntimeBuiltInToolsTests` | 1 | 1 | Verifies root runs receive runtime-owned time and metadata tools over the reachable delegation graph |
| `AgentDefinitionEvaluatorTests` | 4 | 6 | Verifies fixture-backed delegation grading, failed tool selection and answers, pass-rate thresholds, and request validation |
| `TenantStateIsolationTests` | 3 | 3 | Proves tenant isolation, collision-free keys, pod-replacement persistence, and definition-version invalidation |
| `AuthenticatedTenantContextTests` | 4 | 4 | Verifies caller-derived tenant identity, missing/unmapped caller rejection, and opaque Redis keys |
| `AgentRuntimeAuthorizationTests` | 4 | 6 | Verifies delegated scopes, application roles, and caller-to-tenant mapping validation |
| `PostgresAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable publication, active-version changes, duplicate rejection, history, and tenant isolation |
| `PostgresAgentDefinitionStoreIntegrationTests` | 8 | 8 | Against real PostgreSQL: migrations, `jsonb` storage and round trip, sequential and concurrent duplicate-version conflicts, advisory-lock-serialised concurrent activation, the tenant-scoped activation lock, audited deactivation, and tenant isolation |
| `CachedAgentDefinitionStoreTests` | 3 | 3 | Verifies secret-free read-through caching, the cached active-version pointer surviving a database outage, and its invalidation on activation |
| `ConfigurationMcpCredentialStoreTests` | 1 | 1 | Verifies tenant-scoped Authorization header resolution without definition secrets |
| `LiveAgentRuntimeSmokeTests` | 1 | 1 | Opt-in authenticated run, streaming, session inspection and cleanup against a complete live runtime |

Reusable secret-free agent, provider, runtime-definition, and publication builders live in
`AgentDefinitionTestData`. Protocol and built-in tests consume these definition-contract fixtures so
test definitions do not drift into application-specific `AIConfig` setup.
`ScriptedEvaluationAgentFactory` replaces the model provider with a scripted chat client for
evaluation tests.

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 35 | In-memory ASP.NET Core host, execution/control protocols, and startup security tests |
| `Agent Runtime` | 16 | Runtime orchestration, built-in tools, evaluation, definition persistence, and caching without external services |
| `Tenant Isolation` | 14 | Cross-tenant state, JWT permissions, caller mappings, MCP credentials and identity isolation |
| `PostgreSQL` | 8 | Definition store against a disposable `postgres:18-alpine` container; requires Docker |
| `Local Agent Runtime` | 1 | Explicitly enabled smoke test against a complete running runtime |

## Skipped Tests

`LiveAgentRuntimeSmokeTests` is skipped unless explicitly enabled on a non-CI workstation. The
`PostgreSQL` tests need a running Docker engine; exclude them with
`--filter-not-trait 'Category=PostgreSQL'` where none is available.

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

## Layout

```text
Tests/
├── AgentDefinitionTestData.cs
├── ScriptedEvaluationAgentFactory.cs
├── Integration/
│   ├── AgentizrWebApplicationFactory.cs
│   ├── AgentDefinitionAdministrationProtocolTests.cs
│   ├── AgentDefinitionAdministrationWebApplicationFactory.cs
│   ├── AgentRuntimeProtocolTests.cs
│   ├── EndpointSmokeTests.cs
│   ├── HostIntegrationCollection.cs
│   ├── PostgreSqlFixture.cs
│   ├── PostgresAgentDefinitionStoreIntegrationTests.cs
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
    ├── AuthenticatedTenantContextTests.cs
    ├── AgentExecutionCoordinatorTests.cs
    ├── CachedAgentDefinitionStoreTests.cs
    ├── ConfigurationMcpCredentialStoreTests.cs
    ├── InMemoryDistributedCache.cs
    ├── PostgresAgentDefinitionStoreTests.cs
    └── TenantStateIsolationTests.cs
```

## Run

```powershell
dotnet test --project src/CasCap.App.Server.Tests/CasCap.App.Server.Tests.csproj
```

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.App.Server` | Application under test |
| `CasCap.AgentRuntime.Client` | Typed client exercised against the in-memory host |
| `CasCap.Common.Caching` | Redis contracts used by the production state implementations |
| `Microsoft.EntityFrameworkCore.Sqlite` | Credential-free relational definition-store tests |
| `Testcontainers.PostgreSql` | Disposable PostgreSQL server for the `PostgreSQL` category |
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |
