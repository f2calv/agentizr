# CasCap.App.Server

The .NET 10 ASP.NET Core entry host for agentizr. It composes the
[application backend](../CasCap.Backend/README.md) and serves the
[Blazor WebAssembly frontend](../CasCap.App.Client/README.md).

## Quick Start

Follow the [source-run commands](../../README.md#run-from-source) and
[backend configuration examples](../CasCap.Backend/README.md#configuration-examples).
Development can run without PostgreSQL, Redis or authentication; inference still requires a
reachable model provider and an installed agent definition.

## Runtime Roles

The [AppHost](AppHost.cs) selects `agentizr`, `ingestion`, `agentizr,ingestion` or the finite
`migrate` role. The existing `--migrate` argument selects migration-only execution without
starting a listener or worker. Role prerequisites and Debug-only ingestion behavior are documented
in the [backend role reference](../CasCap.Backend/README.md#runtime-roles).

This project owns the executable identity `agentizr`, the
[Program test marker](Program.cs), lifecycle orchestration, entry-assembly configuration and the
[browser shell](Components/App.razor). Backend registration methods preserve application service
ownership and expose only the wiring needed by the host.

## Persistence

The context, design-time factory and migrations live in
[CasCap.Data](../CasCap.Data/README.md). Runtime migration uses the same application image and
external finite role; PostgreSQL schema and migration identifiers remain unchanged.

For offline EF tooling, select Data as both migration and startup project without initializing this host:

```powershell
dotnet ef migrations list --project src\CasCap.Data --startup-project src\CasCap.Data --no-connect
```

Run from the repository root with the repository's tool manifest restored. Applying migrations or
connecting to a database is a separate operation; see [persistence](../../docs/persistence.md).

## Tests

[Server tests](../CasCap.App.Server.Tests/README.md) retain the real
`WebApplicationFactory<Program>` boundary, frontend coverage and infrastructure integration.
[Backend tests](../CasCap.Backend.Tests/README.md) cover application services independently of this
executable.
