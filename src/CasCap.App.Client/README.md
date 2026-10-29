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
| `/apps/{appId}/agents/{agentName}/edit?from={version}` | Copy a version, edit it and publish, with optional guarded activation |
| `/apps/{appId}/new-agent` | Create an agent by publishing its first version, optionally activating it |
| `/apps/{appId}/documents?collection={exactCollectionId}&page=1` | Debug admitted-source catalogue, current jobs and review links |
| `/apps/{appId}/collections/{collectionId}/documents/{documentId}/jobs/{jobId}/review?generationId={optionalSnapshot}` | Current preview or exact immutable review-required snapshot |

Selections and searches live in the URL, so a refresh or shared link reopens the same view.
The editor suggests an unused UTC date-and-revision version. Activation after publication is
unchecked by default and requires confirmation; publication and activation remain separate API
operations. The agent page offers activation for the selected inactive version and deactivation
only when the selected version is active.

New-agent creation offers a server-supplied document-preparation template. It leaves provider
coordinates unconfigured and activation unchecked, preserves its structured-output/stateless
settings, and confirms replacement of an edited draft. The editor exposes the Responses API switch.
See the [template walkthrough](../../docs/administration-frontend.md#built-in-document-preparation-template)
for provider requirements and the still-pending durable-worker integration.

## Public Surface

| Type | Purpose |
| --- | --- |
| [`AdministrationRoutes`](Constants/AdministrationRoutes.cs) | Administration API, sign-in and sign-out routes, the antiforgery header and an escaping path formatter |
| [`ProviderCredentialSources`](Constants/ProviderCredentialSources.cs) | Wire values describing provider authentication without exposing credentials |
| [`AdministrationSessionResponse`](Models/AdministrationSessionResponse.cs) | Sign-in state, display name and the antiforgery request token |
| [`AppSummaryResponse`](Models/AppSummaryResponse.cs) | One app: the tenant boundary, its caller applications and agent count |
| [`AgentSummaryPageResponse`](Models/AgentSummaryPageResponse.cs) | One page of agent summaries |
| [`ProviderCatalogueResponse`](Models/ProviderCatalogueResponse.cs) | Secret-free provider registrations, supported types and the definition policy |
| [`BuiltInDefinitionTemplateResponse`](Models/BuiltInDefinitionTemplateResponse.cs) | Secret-free editable starting definition; discovery does not install or activate it |
| [`AdministrationActivationRequest`](Models/AdministrationActivationRequest.cs) | Activation or rollback guarded by the observed active version |
| [`AdministrationDeactivationRequest`](Models/AdministrationDeactivationRequest.cs) | Deactivation guarded by the observed active version |
| [`AdministrationApiClient`](Services/AdministrationApiClient.cs) | Typed calls that map every HTTP outcome to an `ApiResult<T>` and send the antiforgery token on mutations |
| [`DocumentReviewRoutes`](Constants/DocumentReviewRoutes.cs) | Source/job-qualified preview and immutable review-generation routes |
| [`DocumentCatalogueRoutes`](Constants/DocumentCatalogueRoutes.cs) | App-qualified admitted-source discovery route |
| [`DocumentCataloguePageResponse`](Models/DocumentCataloguePageResponse.cs) | Bounded source metadata, current-job review availability and stored-generation identity |
| [`DocumentReviewGenerationRequest`](Models/DocumentReviewGenerationRequest.cs) | Observed source revision, token budgets and optional versioned structural decisions |
| [`DocumentReviewGenerationResponse`](Models/DocumentReviewGenerationResponse.cs) | Current-source pages/notes and versioned heading-aware projection; never approval |
| [`DefinitionDraft`](Models/DefinitionDraft.cs) | Builds a publication payload that keeps fields the editor does not change |

Definition snapshots, history and evaluation reuse the
[versioned contracts](../CasCap.AgentRuntime.Contracts/README.md).

`GetDocumentReviewAsync` and `StoreDocumentReviewAsync` use the same session and outcome handling
as definition administration. The host currently exposes document-review management only in Debug
with PostgreSQL; it additionally requires actual administrator sign-in, even in Development.
The optional `Review` JSON document uses schema 2 (`version`, `reviewId`, `markdownSha256`,
`exclusions`, `headings`, `layoutTables`). The response's `Projection` retains the profile, exact
structural review, chunks and UTF-16 Markdown provenance without exposing server/provider types.
From an app's **Documents** button, filter admitted sources by exact collection and open a preview
or stored snapshot. The workbench displays escaped Markdown and contextual chunk text, token counts
and Markdown provenance, not rendered HTML or PDF image regions. Empty notes are hidden.
Storing the current preview requires confirmation and preserves its observed source revision and
profile. Success opens the exact immutable generation; failures never automatically repeat a write.
Upload/replace/delete, original-PDF viewing, custom budgets/structural editing and approval are not
implemented. Catalogue admission and stored snapshots do not mean content is searchable.
See the [review walkthrough](../../docs/administration-frontend.md#document-catalogue-and-review).

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Definition, history and evaluation DTOs |
| `Microsoft.AspNetCore.Components.WebAssembly` | Browser runtime host |
| `Microsoft.Extensions.Http` | Typed `HttpClient` registration |
| `MudBlazor` | Component library for tables, forms, dialogs and navigation |
