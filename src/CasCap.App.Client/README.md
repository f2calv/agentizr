# CasCap.App.Client

Browser administration frontend for agentizr, downloaded by the browser and run with Blazor
WebAssembly and [MudBlazor](https://mudblazor.com/). It also holds the administration API routes
and DTOs shared with the server host, which serves both this client and the API.

## Purpose

The client runs entirely in the browser, so everything in it is public. It calls the host's
authorized administration API over HTTP and never references EF Core, execution implementations,
provider SDKs or server configuration. The host enforces every permission and validation rule. The
[frontend walkthrough](../../docs/administration-frontend.md) explains how the host, this client and
the API fit together.

## Pages

| Route | Page |
| --- | --- |
| `/` | Apps the administrator may manage, with caller applications and agent counts |
| `/apps/{appId}` | Paged, searchable agents, including inactive and never-activated ones |
| `/apps/{appId}/providers` | Provider registrations and the definition policy, read-only |
| `/apps/{appId}/agents/{agentName}` | Versions, the selected snapshot, activation audit, activate, roll back and deactivate |
| `/apps/{appId}/agents/{agentName}/edit?from={version}` | Copy a version, edit it and publish a new inactive version |
| `/apps/{appId}/new-agent` | Create an agent by publishing its first inactive version |

Selections and searches live in the URL, so a refresh or shared link reopens the same view.

## Public Surface

| Type | Purpose |
| --- | --- |
| [`AdministrationRoutes`](Constants/AdministrationRoutes.cs) | Administration API, sign-in and sign-out routes, the antiforgery header and an escaping path formatter |
| [`ProviderCredentialSources`](Constants/ProviderCredentialSources.cs) | Wire values describing provider authentication without exposing credentials |
| [`AdministrationSessionResponse`](Models/AdministrationSessionResponse.cs) | Sign-in state, display name and the antiforgery request token |
| [`AppSummaryResponse`](Models/AppSummaryResponse.cs) | One app: the tenant boundary, its caller applications and agent count |
| [`AgentSummaryPageResponse`](Models/AgentSummaryPageResponse.cs) | One page of agent summaries |
| [`ProviderCatalogueResponse`](Models/ProviderCatalogueResponse.cs) | Secret-free provider registrations, supported types and the definition policy |
| [`AdministrationActivationRequest`](Models/AdministrationActivationRequest.cs) | Activation or rollback guarded by the observed active version |
| [`AdministrationDeactivationRequest`](Models/AdministrationDeactivationRequest.cs) | Deactivation guarded by the observed active version |
| [`AdministrationApiClient`](Services/AdministrationApiClient.cs) | Typed calls that map every HTTP outcome to an `ApiResult<T>` and send the antiforgery token on mutations |
| [`DefinitionDraft`](Models/DefinitionDraft.cs) | Builds a publication payload that keeps fields the editor does not change |

Definition snapshots, history and evaluation reuse the
[versioned contracts](../CasCap.AgentRuntime.Contracts/README.md).

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Definition, history and evaluation DTOs |
| `Microsoft.AspNetCore.Components.WebAssembly` | Browser runtime host |
| `Microsoft.Extensions.Http` | Typed `HttpClient` registration |
| `MudBlazor` | Component library for tables, forms, dialogs and navigation |
