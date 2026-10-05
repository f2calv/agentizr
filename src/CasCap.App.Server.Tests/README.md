# CasCap.App.Server.Tests

Credential-free unit and integration tests for the `CasCap.App.Server` host and runtime ownership boundary.

## Purpose

The tests launch the real application through `WebApplicationFactory<Program>` and verify that its public baseline endpoints respond successfully without credentials or external services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 1 | 2 | Verifies `/` and `/healthz` return HTTP 200 |
| `AgentExecutionCoordinatorTests` | 2 | 2 | Verifies definition, credential, override, session, and executor orchestration |
| `TenantStateIsolationTests` | 2 | 2 | Proves tenant isolation and collision-free composite state keys |

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 2 | In-memory ASP.NET Core host tests |
| `Agent Runtime` | 2 | Runtime orchestration without network or external services |
| `Tenant Isolation` | 2 | Cross-tenant state isolation |

## Skipped Tests

There are no skipped tests.

## Layout

```text
Tests/
├── Integration/
│   └── EndpointSmokeTests.cs
└── Unit/
    ├── AgentExecutionCoordinatorTests.cs
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
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |
