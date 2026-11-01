# CasCap.Data

Internal, non-packable .NET 10 PostgreSQL model and migrations for Agent Runtime definitions,
durable state, document processing and shared Data Protection keys. Application authorization,
orchestration and transaction ownership belong to [CasCap.Backend](../CasCap.Backend/README.md).

## Usage

Reference [CasCap.Data.csproj](CasCap.Data.csproj) to use
[AgentRuntimeDbContext](AgentRuntimeDbContext.cs) and its [persistence entities](Entities).
The backend's
[AddAgentRuntimeDatabase](../CasCap.Backend/Extensions/WebApplicationBuilderExtensions.Persistence.cs)
registers the context factory and applies runtime database configuration.
Factory-created contexts are caller-owned; keep related writes inside their existing transaction.

The context retains the `agent_runtime` schema and the
[indexing mappings](AgentRuntimeDbContext.Indexing.cs). Backend services own tenant/actor checks,
PostgreSQL session/advisory locks, cache consistency and document claims. The
[administration registration](../CasCap.Backend/Extensions/WebApplicationBuilderExtensions.Administration.cs)
owns the shared cookie/key-ring application identity; Data supplies its persisted key model.
Runtime contracts, Execution, Evaluation and the browser client do not depend on this library.

## Migrations and Tooling

The context, [14 migrations and snapshot](Migrations) and
[design-time factory](AgentRuntimeDbContextDesignTimeFactory.cs) share the `CasCap.Data` assembly.
EF's default migration discovery follows that assembly.

From the repository root, after restoring the repository's tool manifest:

```powershell
dotnet ef migrations list --project src\CasCap.Data --startup-project src\CasCap.Data --no-connect
```

This is an example, not an executed check. The factory constructs context options without starting
the host. It reads `CasCap__DatabaseConfig__ConnectionString` when supplied; otherwise it uses a
design-time target. Unlike runtime registration, it does not apply `DatabaseName` separately.
Any approved database-changing command must therefore supply the intended database explicitly.

Apply migrations externally through the application's finite `migrate` role before rollout.
See [persistence](../../docs/persistence.md) and the
[backend metadata regression](../CasCap.Backend.Tests/Tests/Integration/PersistenceModelTests.cs).
Offline metadata and SQLite fixtures are not substitutes for isolated PostgreSQL validation.
