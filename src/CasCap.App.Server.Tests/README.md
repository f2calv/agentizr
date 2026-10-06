# CasCap.App.Server.Tests

Credential-free unit and integration tests for the `CasCap.App.Server` host and runtime ownership boundary.

## Purpose

The tests launch the real application through `WebApplicationFactory<Program>` and verify that its public baseline endpoints respond successfully without credentials or external services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 1 | 2 | Verifies `/` and `/healthz` return HTTP 200 |
| `AgentRuntimeProtocolTests` | 6 | 6 | Verifies typed-client execution, live event streaming, validation, and complete session-control lifecycle |
| `TenantSecurityStartupTests` | 3 | 3 | Verifies anonymous JWT rejection and Production requirements for authentication and Redis |
| `AgentExecutionCoordinatorTests` | 3 | 3 | Verifies definition, credential, override, session, bypass, and executor orchestration |
| `TenantStateIsolationTests` | 3 | 3 | Proves tenant isolation, collision-free keys, pod-replacement persistence, and definition-version invalidation |
| `AuthenticatedTenantContextTests` | 3 | 3 | Verifies claim-derived identity, missing-claim rejection, and opaque Redis keys |
| `PostgresAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable publication, active-version changes, duplicate rejection, history, and tenant isolation |
| `CachedAgentDefinitionStoreTests` | 1 | 1 | Verifies secret-free read-through caching and publication invalidation |

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 11 | In-memory ASP.NET Core host, protocol, and startup security tests |
| `Agent Runtime` | 7 | Runtime orchestration, definition persistence, and caching without external services |
| `Tenant Isolation` | 6 | Cross-tenant state and identity isolation |

## Skipped Tests

There are no skipped tests.

## Layout

```text
Tests/
├── Integration/
│   ├── AgentizrWebApplicationFactory.cs
│   ├── AgentRuntimeProtocolTests.cs
│   ├── EndpointSmokeTests.cs
│   ├── HostIntegrationCollection.cs
│   └── TenantSecurityStartupTests.cs
└── Unit/
    ├── AuthenticatedTenantContextTests.cs
    ├── AgentExecutionCoordinatorTests.cs
    ├── CachedAgentDefinitionStoreTests.cs
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
