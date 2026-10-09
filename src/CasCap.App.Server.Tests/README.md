# CasCap.App.Server.Tests

Credential-free unit and integration tests for the `CasCap.App.Server` host and runtime ownership boundary.

## Purpose

The tests launch the real application through `WebApplicationFactory<Program>` and verify that its public baseline endpoints respond successfully without credentials or external services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 1 | 2 | Verifies `/` and `/healthz` return HTTP 200 |
| `AgentRuntimeProtocolTests` | 6 | 6 | Verifies typed-client execution, live event streaming, validation, and complete session-control lifecycle |
| `AgentDefinitionAdministrationProtocolTests` | 10 | 18 | Verifies inactive publication, activation, rollback, audit attribution, strict policy/delegation/credential rejection, conflicts, and bounded history |
| `TenantSecurityStartupTests` | 6 | 6 | Verifies anonymous execution/control-plane rejection, separate execution/admin policies, and Production requirements for authentication, tenant mappings, and Redis |
| `AgentExecutionCoordinatorTests` | 3 | 3 | Verifies definition, credential, override, session, bypass, and executor orchestration |
| `AgentRuntimeBuiltInToolsTests` | 1 | 1 | Verifies root runs receive runtime-owned time and metadata tools over the reachable delegation graph |
| `TenantStateIsolationTests` | 3 | 3 | Proves tenant isolation, collision-free keys, pod-replacement persistence, and definition-version invalidation |
| `AuthenticatedTenantContextTests` | 4 | 4 | Verifies caller-derived tenant identity, missing/unmapped caller rejection, and opaque Redis keys |
| `AgentRuntimeAuthorizationTests` | 4 | 6 | Verifies delegated scopes, application roles, and caller-to-tenant mapping validation |
| `PostgresAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable publication, active-version changes, duplicate rejection, history, and tenant isolation |
| `CachedAgentDefinitionStoreTests` | 1 | 1 | Verifies secret-free read-through caching and activation invalidation |
| `ConfigurationMcpCredentialStoreTests` | 1 | 1 | Verifies tenant-scoped Authorization header resolution without definition secrets |

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 32 | In-memory ASP.NET Core host, execution/control protocols, and startup security tests |
| `Agent Runtime` | 8 | Runtime orchestration, built-in tools, definition persistence, and caching without external services |
| `Tenant Isolation` | 14 | Cross-tenant state, JWT permissions, caller mappings, MCP credentials and identity isolation |

## Skipped Tests

There are no skipped tests.

## Layout

```text
Tests/
├── Integration/
│   ├── AgentizrWebApplicationFactory.cs
│   ├── AgentDefinitionAdministrationProtocolTests.cs
│   ├── AgentDefinitionAdministrationWebApplicationFactory.cs
│   ├── AgentRuntimeProtocolTests.cs
│   ├── EndpointSmokeTests.cs
│   ├── HostIntegrationCollection.cs
│   └── TenantSecurityStartupTests.cs
└── Unit/
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
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |
