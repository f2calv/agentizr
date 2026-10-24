# CasCap.App.Client

Browser administration client for agentizr, downloaded by the browser and run with Blazor
WebAssembly. It holds the same-origin administration API routes and DTOs shared with the server
host, which serves both this client and the API.

## Purpose

The client runs entirely in the browser, so everything in it is public. It calls the host's
authorized administration API over HTTP and never references EF Core, execution implementations,
provider SDKs or server configuration. The host enforces every permission and validation rule.

## Public Surface

| Type | Purpose |
| --- | --- |
| [`AdministrationRoutes`](Constants/AdministrationRoutes.cs) | Administration API route templates and an escaping path formatter |
| [`ProviderCredentialSources`](Constants/ProviderCredentialSources.cs) | Wire values describing provider authentication without exposing credentials |
| [`AppSummaryResponse`](Models/AppSummaryResponse.cs) | One app: the tenant boundary, its caller applications and agent count |
| [`AgentSummaryPageResponse`](Models/AgentSummaryPageResponse.cs) | One page of agent summaries |
| [`ProviderCatalogueResponse`](Models/ProviderCatalogueResponse.cs) | Secret-free provider registrations and the definition policy |
| [`AdministrationActivationRequest`](Models/AdministrationActivationRequest.cs) | Activation or rollback guarded by the observed active version |
| [`AdministrationDeactivationRequest`](Models/AdministrationDeactivationRequest.cs) | Deactivation guarded by the observed active version |

Definition snapshots, history and evaluation reuse the
[versioned contracts](../CasCap.AgentRuntime.Contracts/README.md).

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Definition, history and evaluation DTOs |
| `Microsoft.AspNetCore.Components.WebAssembly` | Browser runtime host |
