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
| [`DocumentCatalogueRoutes`](Constants/DocumentCatalogueRoutes.cs) | App-qualified admitted-source discovery and synchronization routes |
| [`DocumentCataloguePageResponse`](Models/DocumentCataloguePageResponse.cs) | Bounded source metadata, current-job review availability and stored-generation identity |
| [`DocumentCatalogueItemResponse`](Models/DocumentCatalogueItemResponse.cs) | Per-document preparation state and a safe `PreviewConfigured` flag; pinned configuration is not a finished preview |
| [`DocumentSynchronizationResponse`](Models/DocumentSynchronizationResponse.cs) | Synchronization/processing gates, UTC cron and configured-source progress |
| [`DocumentSynchronizationSourceResponse`](Models/DocumentSynchronizationSourceResponse.cs) | Safe counts, conflicts, timestamps and failure codes without Blob coordinates |
| [`DocumentReviewGenerationRequest`](Models/DocumentReviewGenerationRequest.cs) | Observed source revision, token budgets and optional versioned structural decisions |
| [`DocumentReviewGenerationResponse`](Models/DocumentReviewGenerationResponse.cs) | Current-source pages/notes, versioned projection, saved selection and optional readiness assessment; never indexing approval |
| [`DocumentChunkReviewRequest`](Models/DocumentChunkReviewRequest.cs) | Observed source/review revision and excluded candidate ordinals with optional reasons |
| [`DocumentChunkReviewResponse`](Models/DocumentChunkReviewResponse.cs) | Saved selection revision/time, separate exclusions and fixed denied indexing approval |
| [`DocumentChunkSelectionDraft`](Models/DocumentChunkSelectionDraft.cs) | Local inclusion counts, explanations and dirty-state tracking |
| [`DocumentApprovalRequest`](Models/DocumentApprovalRequest.cs) | Explicit classification/attestations and observed source, selection and assessment revisions |
| [`DocumentApprovalResponse`](Models/DocumentApprovalResponse.cs) | Current/stale readiness, derived blockers and fixed denied indexing approval |
| [`DocumentApprovalDraft`](Models/DocumentApprovalDraft.cs) | Mutable assessment form requiring fresh attestations and rationale |
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
On a stored snapshot, include/exclude candidates, filter the list and save a confirmed chunk review.
All candidates start included. Optional exclusion reasons never alter embedding text. Unsaved edits
warn before navigation; observed review revisions prevent concurrent edits from overwriting decisions.
Review records remain separate from the immutable snapshot and do not approve indexing.
After saving a selection, **Document assessment and readiness** records a confirmed readiness
assessment. It retains classification but requires fresh attestations/rationale; editing either form
blocks competing edits, not navigation after an explicit discard confirmation. Source/snapshot and
selection revisions fence the decision. Missing CEFR for vocabulary/exam papers, unresolved gates
and empty selections remain blocked; manuals cannot pass the unimplemented visual-association gate.
Server field validation is shown in the form without losing edits. No readiness decision enables
indexing, invokes a provider or implements future trusted-collection/CEFR retrieval policy.
The saved summary shows the revision/time, recorded attestations and rationale, readiness/blockers
and separate indexing-authorization notice. The new-assessment form is labelled separately:
unchecked fresh attestations and an empty rationale do not erase the saved record.
Upload/replace/delete, original-PDF viewing, custom budgets/structural editing and browser indexing
authorization controls are not offered. Catalogue admission and stored snapshots do not mean content is searchable.
See the [review walkthrough](../../docs/administration-frontend.md#document-catalogue-and-review).

The **Sync documents** panel is independent of catalogue loading and applies to all configured
collections in the app, not the current filter. Confirmation explains that confirmed missing PDFs
lose stored chunks/review content; original blobs are unchanged. Disabled/unmapped sources prevent
submission. A 202 response means the request was persisted, not completed. **Refresh** reads progress
without polling or automatically retrying a mutation; conflicts can remain after a successful scan.
Separate discovery/preparation headings distinguish **Storage scan complete** from
**Preview preparation is paused**. Queued rows distinguish **Preparation setup required** from
**Waiting for preparation** using the pinned-configuration flag; neither means a preview exists.
Technical identifiers and raw states are collapsed, with readable document/preparation labels
remaining visible. Synchronization does not enable paid processing or search.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.AgentRuntime.Contracts` | Definition, history and evaluation DTOs |
| `Microsoft.AspNetCore.Components.WebAssembly` | Browser runtime host |
| `Microsoft.Extensions.Http` | Typed `HttpClient` registration |
| `MudBlazor` | Component library for tables, forms, dialogs and navigation |
