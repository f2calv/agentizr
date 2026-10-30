# CasCap.App.Server

The .NET 10 ASP.NET Core host for agentizr. It owns the multi-tenant execution and definition
control-plane APIs plus Development configuration adapters.

## Quick Start

Use the [source-run commands](../../README.md#run-from-source) with the Development example below,
or the [Helm chart](../../charts/agentizr/README.md) for Kubernetes. Development can run without
PostgreSQL, Redis or authentication, but still needs a reachable model provider for inference.
Tracked settings define no agents.

The runtime resolves a versioned definition and provider credential for the authenticated tenant,
then applies session overrides and coordinates execution and persistence. In non-Development
deployments, PostgreSQL owns definitions and runtime state across replicas; Redis is an optional
cache for API-only operation.

## Runtime Roles

[`FeatureConfig`](Models/_FeatureConfig.cs) selects startup responsibilities through
`CasCap:FeatureConfig:EnabledFeatures` (environment variable
`CasCap__FeatureConfig__EnabledFeatures`). The default is `agentizr`; supported selections are
`agentizr`, `ingestion`, `agentizr,ingestion` and `migrate`. Names are case-insensitive and duplicates
collapse; empty/unknown selections and a migration combined with a daemon fail.

The API/frontend/MCP surfaces below belong only to `agentizr`. `ingestion` exposes status/health
probes and runs a serial elected notification receiver; it requires PostgreSQL, shared Redis
distributed locking, provisioned Azure queues and explicit source mappings. It is a Debug
source-reference PoC: `Queued` is durable admission, not searchable PDF content.
The same elected feature now supports durable job processing behind an off-by-default gate.
Under `CasCap:DocumentIngestionConfig`, `ProcessingEnabled` defaults to `false` and
`ProcessingTimeoutSeconds` defaults to `300` (range 30-900). Enabling processing without a real
[`IDocumentProcessor`](Abstractions/IDocumentProcessor.cs) registration fails startup.
The Debug role registers
[`DocumentPreparationProcessor`](Services/DocumentPreparationProcessor.cs). Jobs without an admitted
hybrid plan use [`PdfDocumentProcessor`](Services/PdfDocumentProcessor.cs), which persists local
immutable chunks. Explicit hybrid plans produce review-only candidates, not chunks or indexes.
Neither path embeds or activates content. Keep the gate off until provider-backed and
authenticated-source checks plus the infrastructure handoff pass.

[`DocumentIngestionJobService`](Services/DocumentIngestionJobService.cs) persists claims,
attempt counts and retry times. Processing is serial, limited to three attempts, with a 30-second
retry delay and expired-claim recovery. Cancellation/leadership loss leaves unfinished work
recoverable; deleted/superseded revisions and expired/replaced claims cannot record completion.
`Completed` means preparation is durably staged, not that a document is searchable; document status
still records admission, not a derived indexing state. `ClaimLost` records no outcome and leaves
recovery/supersession to the existing lifecycle.
Apply the processing and `20261026041415_DocumentPreparedGenerations` migrations externally before
rolling out this code; do not enable processing during a mixed-version ingestion rollout.
Embeddings and searchable generation activation remain future work.

### Document Source Synchronization

The Debug [`DocumentSynchronizationService`](Services/DocumentSynchronizationService.cs) repairs
missed Blob events against durable document/job state, not the contents of transient queues.
`SynchronizationEnabled` defaults to `false`; apply the additive
`20261029104131_AddDocumentSynchronization` migration externally and deploy compatible ingestion
replicas before enabling it. This gate is independent of `ProcessingEnabled`: reconciliation never
calls a preparation or embedding provider, and enabling it does not make PDFs reviewable.

An ingestion owner requests synchronization after every ownership acquisition and at minute zero
each UTC hour. `SynchronizationCron` accepts recurring five-field Cronos expressions and defaults to
`0 * * * *`; `SynchronizationTimeoutSeconds` defaults to `600` (range 30-3600). The shared timing
runner currently requires the Debug sibling checkout, not a published-package assumption.
Requests coalesce durably per configured source; the existing elected loop claims one source at a
time. Database run identity/deadline checks reject expired owners independently of Redis ownership.

The read-only [`Blob inventory`](Services/AzureDocumentBlobInventory.cs) uses the runtime identity
and requires list/read access, but creates no containers, queues or fabricated Event Grid events.
It admits missing PDFs and repairs missing jobs or a queued, unattempted job's absent preparation
plan from trusted source configuration. It does not reset attempts, replay uncertain submissions or
resurrect deleted immutable blob names. Conflicts are counted; a successful scan can still contain
conflicts that require operator attention.

Only a completed inventory followed by an individual `BlobNotFound` permits absence-based deletion.
Partial listings, missing containers, permissions and transport failures leave unobserved content
intact and record a safe failure code. Confirmed deletions and revision replacement remove currently
implemented text/Markdown projections, clear receipt content and revoke pending/active work while
retaining tombstones and usage audit. Existing source/claim fences block stale output publication.
Embedding/search-index purge remains a prerequisite for those future adapters.
Event admission verifies current storage under the existing global transaction lock; slow metadata
calls therefore serialize admissions. Queue redelivery remains safe through durable receipt checks.

Release explicitly rejects ingestion until the required shared APIs are published.
`migrate` is a finite external schema job with no HTTP listener or background ingestion; the existing
`--migrate` argument also selects it.

All roles use the same host/artifact. See
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#d4b-modular-roles-and-elected-ownership)
for the role matrix, exact IaC configuration and deliberately deferred processing/UI work.

The v1 execution response projects execution diagnostics into a stable wire surface: actual provider
model, finish reason, timing, token usage, tool calls, session summary, tool-produced attachments,
and structured delegation/compaction events. Tool arguments and property bags require explicit
diagnostic disclosure on the authenticated request.

Tool composition is tenant-scoped: service tools resolve from request DI, remote MCP connections are
owned for one execution, and sub-agent tools recursively use the same definition and credential
stores with stateless delegated sessions. By default, every depth-zero run also receives runtime-owned
`get_current_datetime_state`, `get_agents`, and `get_providers` tools. Metadata covers only the root
agent's reachable active delegation graph; delegated agents do not receive another copy.

The [document-preparation template](Constants/DocumentPreparationAgentTemplate.cs) is available
through administration without automatic installation or activation. Its opt-in Responses profile
is bounded, tool-free and stateless. The Debug hybrid worker invokes an explicitly approved
tenant-local version; the local PDF processor remains unchanged. Provider-backed checks, quality
acceptance and authorized rollout remain prerequisites in
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#built-in-document-preparation-agent).
For stateless definitions, session mutations and override updates return HTTP 409. Session status,
override reads and permitted instruction disclosure report the effective definition policy without
loading stored conversation state or overrides.

## Offline PDF Preparation

The Debug-only [`PdfDocumentPreparationService`](Services/PdfDocumentPreparationService.cs)
uses PdfPig 0.1.16 and Microsoft.ML.Tokenizers 2.0.1 with the packaged `cl100k_base` vocabulary.
It accepts a borrowed readable stream from its current position, leaves it open and returns explicit
preparation/rejection outcomes. It does not download Blobs, register an ingestion processor or make
documents searchable.

[`DocumentPreparationConfig`](Models/_DocumentPreparationConfig.cs) defaults to 25 MiB,
200 pages, two million extracted UTF-16 characters and 5,000 chunks. Limits reject the whole result
rather than publishing partial chunks. Encrypted/malformed PDFs and wholly no-text inputs are
rejected; mixed documents report pages without extracted text. No-text does not distinguish scanned
pages from blanks, and no OCR is performed.

Default chunking is 512 actual tokens with at most 64-token overlap. A caller can pass explicit
`DocumentChunkingConfig` settings for independently tuned collections without changing the defaults.
Chunks are page-local, retain source-text offsets and have global zero-based ordinals. The returned
[`DocumentPreparationProfile`](Models/DocumentPreparationProfile.cs) captures algorithm, encoding,
library versions and token budgets with a deterministic ID for later generation persistence.
Changing the profile will require reprocessing, not mixing chunks from incompatible profiles.

The current tokenizer is not automatically compatible with every embedding model. Validate its
encoding and budget against the selected embedding model, then evaluate retrieval and the answering
model's context budget; GPU placement alone does not determine chunk size.
Cancellation is checked around synchronous parsing/extraction, not inside it. Resource caps are
not a hard CPU/memory sandbox; see the
[deferred isolation/OCR gates](../../docs/adr-0001-centralized-document-rag.md#poc-limits-and-deliberately-deferred-polishing).

### Offline Markdown-Headings Projection

The Debug-only [`DocumentChunkingService.ChunkMarkdown`](Services/DocumentChunkingService.Markdown.cs)
builds review-only `markdown-headings` chunks from original physical-page Markdown using Markdig
1.5.0 (CommonMark with pipe tables). It does not replace the ingestion processor or persist,
embed, approve or activate a generation; Markdown upload passthrough is not wired.

[`DocumentMarkdownChunkingConfig`](Models/_DocumentPreparationConfig.cs) accepts an explicit
maximum (default 512 tokens), optional soft target (defaulting to the maximum) and paragraph
overlap (default 64 tokens). The target cannot exceed the maximum and overlap must be smaller
than the maximum. The full ancestor-to-leaf heading prefix, separators and body count against
the hard ceiling. A soft target never forces a fitting semantic unit to split.

Parsed heading context carries across contiguous pages and resets across missing physical pages.
Paragraphs may split at sentence/scalar-safe boundaries; list items with nested examples and
tables/code/blockquote blocks remain whole. Oversized structured units fail explicitly for review;
warning/procedure classification and safe table-header/entry subdivision remain separate gates.
No missing heading ancestor or PDF coordinate is invented.

[`DocumentMarkdownChunkingResult`](Models/DocumentMarkdownChunkingResult.cs) carries a versioned
parser/tokenizer/budget profile and a digest of the supplied page numbers and Markdown, not the
original PDF hash. Each chunk records body spans separately from derived heading context.
Offsets are half-open UTF-16 ranges in the supplied page Markdown, not PDF geometry.
Existing preparation resource caps apply; synchronous parsing is not a hard CPU sandbox.

The explicit-review overload accepts a
[`DocumentMarkdownSourceReview`](Models/DocumentMarkdownSourceReview.cs) bound to that exact
Markdown digest, with an opaque review ID and
[`DocumentMarkdownExclusion`](Models/DocumentMarkdownExclusion.cs) entries, reviewed
[heading levels](Models/DocumentMarkdownHeadingReview.cs) and
[layout-table decisions](Models/DocumentMarkdownLayoutTableReview.cs). Supported
[roles](Constants/DocumentMarkdownSourceRoles.cs) are `running-header`, `running-footer` and
`navigation`; reviewed navigation stays source metadata, not embedding input.
No exclusions, heading repairs or layout roles are inferred when a review is absent.

Review schema version 2 requires all three annotation collections (empty when unused) and permits
at most 5,000 decisions in total, a 128-character review ID and a required
reason of at most 1,024 characters per span. Exclusions must identify complete parsed top-level
blocks, use valid scalar-safe source ranges and neither overlap nor repeat. A cross-page block
requires every mapped span with one consistent role. Stale digests, unsupported roles, partial
blocks and unmatched/whitespace-only selections fail explicitly. Decisions of different kinds
cannot overlap either. Mixed body/furniture blocks still require separate review.

Heading decisions assign levels 1-6 to exact single-page headings or promote explicitly verified
single-line paragraphs. They affect only derived heading context, retain the original heading
spans and cannot invent missing ancestors. Layout decisions identify exact single-page pipe tables
with empty column labels. Their nonempty cells become independent Markdown list entries in explicit
`column-major` order, preserving inline markup and escaped pipes. Each list remains one atomic group
within the heading-inclusive budget; oversized groups reject instead of silently splitting entries.
Unreviewed relational tables remain unchanged.

Layout body provenance identifies each actual trimmed cell span in reading order, not table
delimiter syntax; offsets need not increase across columns. Empty parser-synthesized cells have no
invented source span. Unmapped nonformatting content rejects. These are reviewed projections,
not heuristic table classification or rewritten normalized Markdown.

The original text, offsets and retained block ordinals remain unchanged. Heading context can
continue past an explicitly excluded running title. Profile version 3 identifies this interpretation;
the result's `Id` additionally fences the source digest and exact review decisions, while
`MarkdownSha256` remains the unmodified source digest. Serialized review metadata retains each
excluded role/range/reason and structural decisions outside the embedding text. An all-excluded result contains no chunks,
not an approved or indexed generation.
Archived schema-version-1 reviews are not reinterpreted; issue a new exact-source version-2 review
to use the current projection.

This internal review-only contract does not authenticate reviewers or accept annotations from
document content. A future management action must authorize the reviewer and tenant/source scope.
It enables no ingestion, correction UI or navigation retrieval API. Explicit durable review storage
is a separate operation below, not a side effect of chunking.

### Review-Required Markdown Generations

The Debug-only [`DocumentPreparationMarkdownStore`](Services/DocumentPreparationMarkdownStore.cs)
explicitly persists normalized candidates and reviewed heading-aware projections from existing
validated hybrid receipts. It is registered for ingestion but not invoked automatically by the
worker, exposed through HTTP/UI or authorized by a source claim alone. Management must separately
authenticate and authorize the reviewer, tenant and document.

Storage requires a finished `ReviewRequired` job and the exact current tenant/collection/document,
job, source sequence, ETag, Blob URI and admitted plan. A released/expired processing lease is not
reused as authorization. Receipt stages, physical pages, completion/model/usage contracts and
resource bounds are revalidated without provider calls. Projection happens outside the mutation
lock; the transaction rechecks current source and receipt bindings under the shared admission
advisory lock before insertion, then rechecks the source before commit.

The separate `agent_runtime.document_markdown_generations` table stores a version-1 JSONB
[snapshot](Models/DocumentMarkdownReviewGeneration.cs) with source/plan hashes, original receipt
references, normalized page Markdown, separate change/uncertainty notes, profile, review decisions,
heading context and ordered multi-span body provenance. Original paid receipts remain unchanged.
Generation identity binds ownership, job/source revision and the typed snapshot: retries reuse the
row; changing the review or chunking profile appends another. JSONB object ordering/whitespace is
not the original receipt serialization, so its archived raw hash is not recomputed from loaded
JSON; generation identity uses typed reserialization.

Reads require the same current source/job fence; replacement/deletion hides retained historical
rows. All snapshots remain review-required, including an empty/all-excluded projection. No old
page-local chunks, approval, active pointer, embedding or search eligibility is created.
Provenance is normalized Markdown offsets and original physical pages, not fabricated PDF regions.

Receipt-width checks explicitly convert JSONB to SQL text on Npgsql and retain SQLite's supported
string expression. Row and payload bounds stay server-side; loaded UTF-16 totals are checked again.
The [PostgreSQL acceptance tests](../CasCap.App.Server.Tests/Tests/Integration/PostgresDocumentMarkdownGenerationTests.cs)
prove actual JSONB round-trips, concurrent retries/review/profile changes, both admission orderings,
receipt/source fencing, cancellation, rollback, timestamps and composite ownership constraints.

The additive `20261028120307_DocumentMarkdownReviewGenerations` migration has been checked and
applied only to isolated fixture-owned databases, including an upgrade retaining existing paid
receipts. The separately approved retained local paid ledger also has the review-generation
migration and two real-document review-required generations. No deployed database has been
migrated; semantic/visual/retrieval acceptance remains separate.

### Protected Review Management

The Debug-only [administration controller](Controllers/AdministrationDocumentReviewsController.cs)
uses the existing app-administrator assignment and cookie, plus a mandatory authenticated-reviewer
policy. Anonymous Development access and machine bearer identities cannot use it. Every mutation
requires the session's antiforgery token. Review and catalogue responses carry `Cache-Control: no-store`.

The [catalogue controller](Controllers/AdministrationDocumentsController.cs) exposes
`GET /admin/api/apps/{appId}/documents?page=1&pageSize=25&collection={exactCollectionId}`.
The collection filter is optional; page is 1-10,000 and page size is 1-100. Its
[query service](Services/DocumentCatalogueAdministrationService.cs) reads bounded metadata for
admitted sources, their current jobs and a current-source stored-generation identity. It never
loads receipt/content payloads or returns Blob URIs and plans.
[`DocumentCatalogueItemResponse.PreviewConfigured`](../CasCap.App.Client/Models/DocumentCatalogueItemResponse.cs)
reports only whether the current job has a pinned preparation plan. It does not assert that
processing is enabled, the plan has executed or a review preview exists. Admission state is not
indexing state.

The [synchronization controller](Controllers/AdministrationDocumentSynchronizationController.cs)
uses the same cookie/assignment policies, antiforgery protection and no-store responses:

| Method / route | Behavior |
| --- | --- |
| `GET /admin/api/apps/{appId}/documents/synchronization` | Read configured-source progress, counts, conflicts, timestamps and safe failure codes |
| `POST /admin/api/apps/{appId}/documents/synchronization` | Coalesce durable requests for all configured collections; return 202 with a status Location |

The [administration service](Services/DocumentSynchronizationAdministrationService.cs) derives
tenancy from the authorized app, not request fields. HTTP replicas persist requests only; they
require neither Blob/queue transports nor a local ingestion worker. Source URIs, plans and other
tenants' state never enter responses. Missing storage returns 501; requesting synchronization while
disabled or without mapped sources also returns 501. Refresh reads status; do not automatically
repeat a POST after an uncertain response. See the reconciliation gate and deletion contract above.

Under `/admin/api/apps/{appId}/collections/{collectionId}/documents/{documentId}/jobs/{jobId}/review`:

| Method / suffix | Behavior |
| --- | --- |
| `GET` | Preview validated normalized pages, separate notes and default heading-aware chunks; no persistence |
| `POST /generations` | Store or reuse an immutable review-required projection with the observed sequence/ETag, explicit token budgets and optional schema-2 structural review |
| `GET /generations/{generationId}` | Read an exact generation only while its finished job and source remain current |
| `PUT /generations/{generationId}/chunk-review` | Append authenticated inclusion/exclusion decisions using the observed source and review revision; never approve indexing |

The [service](Services/DocumentReviewAdministrationService.cs) derives the tenant from the
authorized app and loads source URI, plan and job metadata from the database, never request JSON.
Wrong ownership or unavailable review material returns 404; stale source observations return 409;
invalid budgets/decisions return 400. Receipt corruption remains a server error, not a bad-input
fallback. Missing PostgreSQL support returns 501. No Redis or ingestion worker is required for
these management operations, and they make no preparation/provider calls.

Responses allowlist pages/notes, PDF digest, current sequence/ETag and a versioned projection
document. They exclude raw provider receipts, usage, deployment coordinates and plan metadata.
Projection spans remain Markdown offsets, not precise PDF geometry. Review IDs are structural
decision identities, not authenticated actor records or semantic approvals; approval and its audit
remain separate work. The [browser workbench](../../docs/administration-frontend.md#document-catalogue-and-review)
lists admitted sources, displays escaped Markdown/chunks and confirms storage of the observed
default-profile preview. Stored snapshots also offer confirmed chunk-selection reviews, counts,
included/excluded filters and optional exclusion reasons, without editing candidate content.
It offers no upload, replacement, deletion, original-PDF viewer,
custom profile/structural editing or approval. See the
[manual requests](../../requests/document-reviews.http) for the cookie/antiforgery prerequisites.

The additive `DocumentChunkReviews` migration creates `agent_runtime.document_chunk_reviews`
and a tenant-qualified snapshot key. Review history is append-only, attributed through the validated
actor context and serialized with admission/revocation through the existing PostgreSQL advisory lock.
The exact generation and source remain fenced; selection revisions use optimistic concurrency.
An identical current retry by the same actor reuses its receipt, including after a JSONB round-trip.
Deletion of a snapshot cascades to its selection records. No snapshot payload, preparation receipt
or job state is rewritten. Apply the migration externally before rollout; selection remains
review-required with indexing denied until separate classification, safety and provenance gates pass.

The [native HTTP acceptance](../CasCap.App.Server.Tests/Tests/Integration/PostgresAdministrationDocumentReviewTests.cs)
verifies persisted JSONB source resolution, concurrent retries, ownership fences, source revocation,
advisory-lock conflicts and post-insert rollback against isolated PostgreSQL. Source lookup
materializes only persisted fields before adding released-lease defaults in application code.
The separately gated
[retained-document acceptance](../CasCap.App.Server.Tests/Tests/Integration/RetainedDocumentReviewAcceptanceTests.cs)
also stores the six retained pages as two review-required generations without repeating preparation:
23 manual chunks and ten vocabulary chunks match the verified structural projections exactly.
It preserves source/job records, paid receipts and checked private inputs. Authentication is a
test cookie stand-in. An additional synthetic native acceptance case uses actual encrypted cookies,
PostgreSQL-backed Data Protection and antiforgery across two independent HTTP hosts.
These checks do not establish deployed OIDC/callback behavior or semantic/visual approval.
The manual's figure associations remain unresolved.

### Durable Hybrid Preparation

Agentizr ships the [`document-preparation` definition template](Constants/DocumentPreparationAgentTemplate.cs)
through the admin provider catalogue and new-agent editor. It is the authoritative shipped
reconciliation prompt/schema, using Azure OpenAI Responses with bounded, stateless, tool-free
execution. Select a tenant-authorized provider, endpoint and model, then explicitly publish and
activate an immutable tenant-local version. The template supplies no deployment credentials or
paid model defaults and never installs/activates itself.

An optional [`DocumentSourceConfig.AgentPreparation`](Models/_DocumentIngestionConfig.cs) authorizes
the managed-layout/preparation-agent path for new admissions. It has no paid defaults. Admission
pins the exact active tenant-local snapshot, model/revision/resource binding and explicit limits
inside the inbox transaction. Changing source configuration or the active definition does not
retarget already-admitted jobs; old/null plans stay local.

`AgentPreparation.PhysicalPages` optionally pins distinct one-based original page numbers.
An empty array retains whole-document processing. `MaximumPages` limits the selected-page count;
the separate preparation resource limit still bounds the full PDF page count. Selections are
validated before paid intents, processed in original-page order and sent unchanged to layout
analysis and the page-specific response schema. Missing/out-of-range pages are rejected before
submission. The original Blob URI, ETag and full-byte SHA-256 remain authoritative; no cropped
bytes are substituted under the original identity.

[`HybridDocumentProcessor`](Services/HybridDocumentProcessor.cs) conditionally downloads and bounds
the original PDF, including scanned/blank pages. Before each new paid stage it checks current tenant
provider/endpoint authorization and bounded read-only ARM account/deployment metadata. The runtime
identity therefore needs metadata read access as well as approved data-plane access. A base model
name in a Responses result alone is not model-revision proof.

The [`operation store`](Services/DocumentPreparationOperationStore.cs) commits one intent before
each layout POST or physical-page agent call. Managed receipts resume with GET only; recorded
model responses are reused, including rejected/incomplete output and usage. Request timeouts stay
inside the existing claim deadline. A missing receipt is ambiguous and blocks in
`ReconciliationRequired`, never an automatic paid retry. Late five-second-bounded accounting writes
cannot complete a stale claim. There is no operator replay/unblock API yet; retain the records and
obtain explicit reconciliation/new-generation authorization rather than editing away intents.

Logical page identity includes original bytes, physical page, plan, draft and copier version.
The first submitted derived-PDF digest is retained separately: PDF writer metadata changes between
copies, so fresh copied-byte hashes cannot identify safe replay. Candidates finish only as
`ReviewRequired`; schema validity is not fidelity approval, chunk preparation or search activation.
The runtime adapter uses worker-local tenancy and the actual framework executor, without HTTP
tenant context, tools, sessions, overrides or generic content history.

The [`candidate validator`](Services/DocumentPreparationCandidateValidator.cs) normalizes line endings,
HTML whitespace entities such as `&nbsp;` and nonbreaking-space characters in the Markdown projection.
Raw responses remain unchanged in the ledger. Other escaped source characters are preserved rather
than blindly decoding/deleting markup and changing its meaning. The `PdfReconciliationV2` template
instructs the agent to emit Markdown without HTML layout or appended review/usage sections.
Only `Markdown` is document content; `Changes`, `Uncertainties` and native usage are separate review
or accounting metadata, not chunk input. Hybrid candidates still stop before chunking.
The private acceptance preview hides empty notes, labels nonempty **Page Uncertainties**, collapses
change notes and leaves usage in separate exports. It is not the administration document UI.
Existing immutable definitions/plans retain their instructions until a new version is explicitly
published/activated and used for new admissions.

The ledger retains managed page units and reported model token/cache/reasoning counters when
available. Missing counters remain unknown; no currency estimates or invoice totals are calculated.
Document preview/comparison and cost presentation remain future administration UI work.
Apply the additive hybrid-preparation migration externally before rollout. Do not mix old ingestion
replicas with enabled hybrid bindings.

Admission currently accepts only PDFs. Markdown notifications are quarantined; the planned
[UTF-8 passthrough](../../docs/adr-0001-centralized-document-rag.md#markdown-source-handling) skips
PDF/managed/model conversion rather than converting Markdown back to Markdown.

### Conditional Blob Sources

[`DocumentBlobDownloadService`](Services/DocumentBlobDownloadService.cs) is registered for the
Debug ingestion role. It revalidates each claim against the configured tenant/collection/container
binding before opening a canonical credential-free HTTPS PDF location. Blob names are decoded once
and passed to `CasCap.Api.Azure.Storage`'s `OpenBlobReadAsync` with the admitted ETag.
The library uses conditional range reads, not a whole-file download or a container-creating
constructor; authentication uses the existing explicitly configured runtime identity.

Missing ETags, unassigned sources or relative path segments in decoded names fail explicitly.
Azure 404/412, permissions and transient
failures propagate during opening or reading; the future processor must classify outcomes and
discard partial data. The caller owns the stream and supplies read cancellation and preparation
limits. The PDF processor consumes this stream before persisting complete preparation output.
The adapter itself does not mark jobs complete or activate generations.
No Azure access was performed for its offline tests.
Its new shared APIs remain unpublished, so keep this path on Debug project references.

### Durable Prepared Output

[`DocumentPreparationStoreService`](Services/DocumentPreparationStoreService.cs) atomically stages
generation metadata and every chunk. A deterministic source/profile identity deduplicates retries,
including recovery after staging committed but job completion did not. Stored metadata includes the
original PDF SHA-256, source revision/ETag, complete preparation profile, page count and no-text pages.
Separate chunk rows retain text, page, ordinal, offsets and actual token count.

The store and job completion share the exact tenant/collection/document/source/claim fence under
the existing PostgreSQL transaction lock. The store rechecks the database deadline after saving,
before commit. A stale claim returns `ClaimLost`; cancellation, deadline expiry during inserts and
write failures roll back the batch. Tenant-qualified foreign keys protect generation ownership.
No searchable pointer or vector is written, and changing a profile creates distinct output.

The processor treats invalid source metadata, absent/changed Blobs and rejected PDFs as permanent
failures. Source 408/429/5xx and read I/O failures use bounded retries; unknown permissions,
database faults and cancellation propagate. Durable cause codes remain generic.
Existing prepared text/history is retained after tombstones; authorized purge/retention and a
searchable-generation lifecycle remain prerequisites for broader use.

## Public Surface

| Endpoint | Response |
| --- | --- |
| `/status` | Host identity |
| `/healthz` | ASP.NET Core health status |
| `/`, `/apps/...` | [Administration frontend](../../docs/administration-frontend.md) shell and WebAssembly assets, served when `AdministrationConfig` is enabled or in open Development; otherwise a setup page listing the missing settings |
| `/mcp` | Opt-in Streamable HTTP MCP endpoint (`CasCap:McpConfig:Enabled`, default off) serving the read-only `get_agentizr_status` tool: service name, version, commit and UTC time only. Requests carrying an `Origin` header are rejected with 403; when disabled the route does not exist |
| `POST /api/v1/agents/{agentName}/runs` | Executes one tenant-scoped agent turn |
| `POST /api/v1/agents/{agentName}/runs/stream` | Streams live execution events followed by the final response |
| `GET/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}` | Inspects or resets active session state |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/compact` | Compacts active session history |
| `PUT/DELETE /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}` | Saves or deletes a named snapshot |
| `POST /api/v1/agents/{agentName}/sessions/{sessionId}/snapshots/{snapshotName}/activate` | Loads a named snapshot |
| `GET/PUT /api/v1/agents/{agentName}/sessions/{sessionId}/overrides` | Gets or replaces complete runtime overrides |
| `GET /api/v1/agents/{agentName}/sessions/{sessionId}/instructions` | Gets the effective instructions (override, else active definition) when the tenant enables disclosure; otherwise 404 |
| `POST/GET /api/v1/agents/{agentName}/definitions` | Publishes an inactive immutable snapshot or lists history |
| `GET /api/v1/agents/{agentName}/definitions/{definitionVersion}` | Gets one immutable snapshot |
| `GET /api/v1/agents/{agentName}/definitions/active` | Gets the active snapshot |
| `POST /api/v1/agents/{agentName}/definitions/{definitionVersion}/evaluate` | Grades a version against fixture scenarios without activating it |
| `PUT /api/v1/agents/{agentName}/definitions/{definitionVersion}/activate` | Activates or rolls back to a version, optionally gated by an evaluation |
| `POST /api/v1/agents/{agentName}/definitions/active/deactivate` | Deactivates the active version with an audited reason; refused while another active agent delegates to it |
| `GET /api/v1/agents/{agentName}/definitions/activations` | Gets activation and rollback audit history |
| `GET /admin/api/session` | Describes the administrator session and issues an antiforgery token |
| `GET /admin/login`, `POST /admin/logout` | Browser navigations that start OpenID Connect sign-in or end the session |
| `GET /admin/api/apps` | Lists the apps the administrator may manage, with caller applications and agent counts |
| `GET /admin/api/apps/{appId}/agents` | Pages the app's agents, including inactive and never-activated agents |
| `GET /admin/api/apps/{appId}/providers` | Gets secret-free provider registrations and the definition policy |
| `/admin/api/apps/{appId}/agents/{agentName}/definitions/...` | Mirrors the definition routes above for the selected app; activation and deactivation require the administrator's observed active version and return 409 when it changed |

The application API uses thin
[`AgentRuntimeController`](Controllers/AgentRuntimeController.cs) and
[`AgentDefinitionsController`](Controllers/AgentDefinitionsController.cs) actions. Each delegates to
one typed method on [`AgentRuntimeApiService`](Services/AgentRuntimeApiService.cs) or
[`AgentDefinitionApiService`](Services/AgentDefinitionApiService.cs), which owns DTO projection
and streaming orchestration. Services return DTOs or typed outcomes; controllers select the HTTP
response with `TypedResults`. Concrete return types provide response metadata without duplicate
success/error attributes. Explicit metadata covers controller validation, session-conflict handling
and dynamic status codes.

The administration API serves the browser frontend from
[`AdministrationController`](Controllers/AdministrationController.cs) and
[`AdministrationDefinitionsController`](Controllers/AdministrationDefinitionsController.cs) through
[`AdministrationApiService`](Services/AdministrationApiService.cs). Its route and DTO contracts live in
the [browser client project](../CasCap.App.Client/README.md), not the published contracts package.
Every action requires the app-administrator policy: the route's `appId` is only a requested target
until [`AdministrationAccessService`](Services/AdministrationAccessService.cs) confirms the user is
assigned to it, after which the app becomes the tenant for that request. Failed authorization returns
401 or 403 Problem Details rather than a redirect.

Routes remain defined by the version 1 contract, and endpoint names are stable. Controller validation
checks request DTOs before execution. The streaming action returns the existing JSON-array stream
without buffering it into a collection. The host retains its small status, health and MCP mappings.

API services and authorization policies are registered by
[`AddAgentRuntimeApi`](Extensions/ServiceCollectionExtensions.cs). Execution, definition reads,
publication and activation retain separate permissions. Only Development with authentication
explicitly disabled permits anonymous controller access; non-Development startup still fails closed.
Administration endpoints are excluded from that anonymous convention and are open only when
`AdministrationConfig` is also disabled.

The runtime derives tenancy from
[`ITenantContext`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.App.Server/Abstractions/ITenantContext.cs), never from the
request body. `/healthz` uses the host's health checks; it does not probe the model provider,
PostgreSQL or Redis.

## Configuration

The opt-in [runtime logging soak](../../docs/runtime-configuration-soak.md) uses typed PostgreSQL
configuration and the standard options monitor without restarting the host. It is currently limited
to Debug/local-source builds; normal Release builds remain unchanged and reject attempts to enable
the unpublished feature. The root deployment script already selects Debug and its sibling project
references. No configuration store is contacted unless the soak is explicitly enabled.

`AppConfig` provides safe defaults for the telemetry meter prefix and service name. Set `AppConfig__OtlpExporterEndpoint` to enable OTLP export; leaving it unset disables the shared telemetry registration.

`AgentRuntimeConfig` binds from `CasCap:AgentRuntimeConfig`. Definitions are grouped under `Tenants`,
each with an explicit `DefinitionVersion`, provider dictionary, and agent dictionary. The tracked
configuration contains no tenant identifiers or credentials and is used only by the read-only
Development adapter. `TenantAgentConfig.TimeZoneId` configures the built-in tenant-local date/time
tool and defaults to UTC. `TenantAgentConfig.CurrentTimeContextEnabled` (default `true`) adds a
line such as `[Current local time: Monday 2026-10-19 06:15 (UTC, UTC+00:00)]` to the user
message of every run, including delegated runs, so agents know the date without a tool call. It
travels with the message rather than the instructions, which keeps instructions identical between
runs for provider prompt caching and dates each stored turn.
`TenantAgentConfig.InstructionsDisclosureEnabled` (default `false`) lets
`agent.execute` callers read their own agents' effective instructions through the session
`instructions` route, so a chat host can show them without holding `agent.definition.read`.

Outside Development, `DatabaseConfig` (`CasCap:DatabaseConfig`) supplies PostgreSQL. `DatabaseName`
optionally replaces the database in `ConnectionString`, so applications sharing one server
connection string each use their own database. Immutable definition
snapshots store schema-versioned JSONB plus publisher/reason metadata, while a separate active
pointer selects one version per tenant agent. Publication never changes live traffic; activation
atomically advances the pointer and appends actor/reason audit history. Redis is a read-through
cache, not the authority.

`TenantAuthenticationConfig` binds from `CasCap:TenantAuthenticationConfig`. When enabled, JWT
validation maps the configured caller claim (`azp` by default) through an explicit tenant/caller
allowlist to derive `ITenantContext.TenantId`. A caller identifier can belong to exactly one tenant.
Authentication and PostgreSQL are mandatory outside Development; startup fails closed when either
is missing.

Execution and session operations require `agent.execute`. Definition administration uses separate
`agent.definition.read`, `agent.definition.publish` and `agent.definition.activate` permissions.
Permissions are accepted from delegated `scope`/`scp` claims or application `roles`. Publisher and
activation actor identifiers come from validated principal claims, never request bodies. Provider credentials remain behind
[`IProviderCredentialStore`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.App.Server/Abstractions/IProviderCredentialStore.cs)
and definition publication rejects embedded API keys.

`AdministrationConfig` binds from `CasCap:AdministrationConfig` and controls the browser frontend.
`AppAdministrators` maps each app to the user identifiers, read from `AdministratorClaimType`
(`oid` by default), who may read, publish and activate its definitions. An app is the tenant
boundary shared by `TenantAuthenticationConfig.TenantCallers` and `AgentRuntimeConfig.Tenants`.
Assignments are configuration-only; machine callers and their scopes never grant administration. An assignment alone never creates an app: an app the runtime no longer defines, such as one whose caller mapping was removed or disabled, is ignored.

When enabled, administrators sign in with an OpenID Connect authorization-code flow with PKCE
against a dedicated web registration (`Authority`, `ClientId` and a secret-supplied `ClientSecret`),
and the host issues its own Secure, HttpOnly `agentizr-admin` cookie. Identity tokens are not saved
and never reach the browser. Register the exact HTTPS `CallbackPath` (`/signin-oidc`) and
`SignedOutCallbackPath` (`/signout-callback-oidc`) redirect URIs. Behind a TLS-terminating proxy, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the callbacks keep their HTTPS scheme. Administration
mutations and sign-out require the antiforgery token from `/admin/api/session`, sent in the
`X-XSRF-TOKEN` header or the sign-out form. With PostgreSQL configured, the Data Protection key ring
that protects cookies and antiforgery tokens is shared through the `data_protection_keys` table, so
any replica accepts them; the keys are not yet encrypted at rest.

`AgentDefinitionPolicyConfig` limits publication to supported schema versions and explicit provider,
remote MCP and in-process service-tool allowlists. Version 1 requires inline instructions, rejects
arbitrary settings and prompt sources, uses strict JSON member handling, and rejects URI credentials,
queries and fragments. Activation verifies delegated agents exist, rejects cycles and caps delegation
depth; execution enforces the same cap. The delegation graph is revalidated under a tenant-scoped
PostgreSQL advisory lock, so concurrent activations of different agents cannot form a cycle.
`AgentRuntimeConfig.MaximumToolInvocationsPerRun` (default 50) bounds tool calls, including
delegations, across a run and all of its sub-agents; further calls return an error result so the
model answers with what it has. Tracked defaults permit only local Ollama and no MCP/service
tools.

`AgentRuntimeConfig.SessionCompaction` sets the session compaction policy per provider name for
every tenant, and `TenantAgentConfig.SessionCompaction` replaces an entry for one tenant. Each
`SessionCompactionConfig` strips tool calls and results on every run, trims the history to
`TargetMessageCount` non-system messages once it exceeds `TriggerMessageCount` (both default 20),
and reports `session.compacted` events according to `Notification`: `Off`, `WindowTrimmed`
(default) or `Always`. Startup fails when a trigger is below its target. See
[Session Compaction](../../docs/session-compaction.md) for the measurements behind the settings.

Remote MCP definitions store only `ToolSource.Credential`, a logical tenant-local name. The runtime
resolves its `Scheme` and secret `Parameter` from `AgentRuntimeConfig:Tenants:{tenant}:McpCredentials`
through the final private configuration provider, validates availability before activation, and
passes the resulting Authorization header only to the per-run MCP transport. A `Basic` credential
without a `Parameter` is built from the runtime's shared `CasCap:ApiAuthConfig` username and
password.

Provider API keys resolve the same way from `AgentRuntimeConfig:Tenants:{tenant}:Providers:{name}:ApiKey`.
A tenant can instead list a provider name in `AgentRuntimeConfig:Tenants:{tenant}:EntraIdProviders`,
which authenticates that Azure OpenAI provider with the runtime's own Microsoft Entra identity (the
`AzureAuthConfig` identity used for Key Vault). Use it for resources with key authentication
disabled, and grant the identity an OpenAI user role on the resource. No tenant inherits it implicitly.

### Definition Evaluation

An evaluation puts fixture-backed scenarios to one immutable version, inactive or not, through its
real provider and model-facing tool surface, then grades each run's answer and tool selection with
the `CasCap.AgentRuntime.Evaluation` grader. No external tool executes: read-only remote MCP, service
and delegation tools return the scenario's `ToolResponses` fixture (or a no-data response), and
actuating remote tools are recorded and sandboxed. Only the runtime's read-only built-ins run for
real. Delegation tools keep their production name and description but are never invoked, so each
definition is evaluated in isolation.

The evaluate endpoint requires `agent.definition.activate`. Activation accepts an optional
`Evaluation` and refuses the version, with a summary of the failed runs, when the pass rate falls
below `MinimumPassRate`. Set `AgentDefinitionPolicyConfig.RequireEvaluationBeforeActivation` to
refuse every activation, including rollback, that carries no evaluation.
`EvaluationRunTimeoutSeconds` bounds each run. Results are returned to the caller and not persisted.

Sessions, snapshots and overrides use PostgreSQL storage behind the existing store abstractions,
with a durable revision check before reading their Redis-cached payloads. Runtime mutations hold a
connection-bound PostgreSQL session advisory lock; overlapping requests return HTTP 409.
`AgentRuntimeConfig.StateSlidingExpirationHours` controls only Redis payload TTL, not durable
retention. Development without PostgreSQL uses process-local stores.

The definition version participates in every session and override key, so changing the active
version invalidates state by namespace without scanning Redis. Previous snapshots and state remain
available for rollback until explicitly removed.

Immutable definition contents are cached with `DefinitionCacheExpirationMinutes` (default `60`,
sliding expiry). `ActiveDefinitionCacheSeconds` bounds active-pointer cache age (default `5`,
absolute expiry; `0` disables it), with invalidation after activation/deactivation. An already-started
run keeps its selected immutable version. Session ownership/state still requires PostgreSQL.

`ConversationHistoryEnabled` (default `false`) opts into durable debugging history: user/model text,
tool calls/results, partial failed activity and turn outcomes. Reset/compaction leaves that archive
intact. See [persistence](../../docs/persistence.md) for privacy, tables and Compose test setup, and
[high availability](../../docs/high-availability.md) for concurrency and cutover constraints.

Runtime turns and session mutations acquire exclusive tenant/agent/session ownership across
definition versions. Overlap returns HTTP 409 without waiting. State writes use the owning
PostgreSQL connection and cannot reconnect after ownership is lost. Development without PostgreSQL
uses process-local coordination. See [high availability](../../docs/high-availability.md) for rollout
and failure constraints.

Named session snapshots use their own opaque state namespace. Session inspection and compaction are
performed inside the runtime through Agent Framework serialization; raw session JSON never crosses
the HTTP boundary.

EF migrations are applied externally and create schema only. The model deliberately contains no
`HasData` payload.

## Configuration Examples

For offline Debug preparation, defaults require no settings. This optional merge fragment shows
all preparation settings and a smaller token budget; it neither enables ingestion nor selects an
embedding model:

```json
{
  "CasCap": {
    "DocumentPreparationConfig": {
      "MaximumPdfBytes": 26214400,
      "MaximumPages": 200,
      "MaximumExtractedCharacters": 2000000,
      "MaximumChunks": 5000,
      "Chunking": {
        "MaximumTokensPerChunk": 256,
        "OverlapTokens": 32
      }
    }
  }
}
```

Chunk budgets must be 8-8192 tokens, with nonnegative overlap strictly below the budget.
Settings are validated at ingestion startup; per-call overrides are validated on preparation.

The following is the optional `AgentPreparation` object on an **existing authorized source** in
`DocumentIngestionConfig.Sources`, not a complete source mapping or enablement instruction:

```json
{
  "AgentPreparation": {
    "AgentName": "example-preparation",
    "DefinitionVersion": "ExampleV1",
    "Model": "example-model",
    "ModelRevision": "example-revision",
    "DeploymentResourceId": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/example/providers/Microsoft.CognitiveServices/accounts/example/deployments/example-deployment",
    "ManagementEndpoint": "https://management.azure.com/",
    "DocumentIntelligenceEndpoint": "https://layout.example.com/",
    "MaximumPages": 6,
    "MaximumOutputTokens": 8192,
    "RequestTimeoutSeconds": 120
  }
}
```

Publish/activate the matching tool-free stateless template version separately, configure its tenant
credential registration and allowed provider authority, and verify the exact deployed model revision.
Limits require explicit values: pages 1-200, output tokens 1-8192 and request seconds 1-600.
The agent's own output ceiling can only lower this budget. The hybrid also caps source/derived PDFs
at 25 MiB and retains the configured overall character limit. This fragment changes no credentials,
definitions, `ProcessingEnabled` gate or runtime-role selection.

For Development, add this synthetic bootstrap to the gitignored `appsettings.Local.Development.json`
at the repository root. The example assumes a local Ollama server with `example-model` already
loaded; replace the model name with one installed on your server.

```json
{
  "CasCap": {
    "AgentRuntimeConfig": {
      "DefaultTenantId": "example-tenant",
      "Tenants": {
        "example-tenant": {
          "Providers": {
            "Local": {
              "Type": "Ollama",
              "Endpoint": "http://localhost:11434",
              "ModelName": "example-model"
            }
          },
          "Agents": {
            "assistant": {
              "Provider": "Local",
              "Name": "assistant",
              "Description": "Example assistant",
              "Prompt": "Respond to the user's request.",
              "Instructions": "Give concise, accurate answers."
            }
          }
        }
      }
    }
  }
}
```

This uses the default definition version `1` and process-local state. It supports execution and
definition reads; publication, activation and deactivation require PostgreSQL and return 501 with
the read-only configuration adapter.

For an authenticated deployment, the following non-secret settings illustrate caller mapping,
publication policy and state tuning. They supplement, not replace, secret-supplied connections
and credentials:

```json
{
  "CasCap": {
    "TenantAuthenticationConfig": {
      "Enabled": true,
      "Authority": "https://identity.example.com/",
      "Audience": "api://example-runtime",
      "TenantCallers": {
        "example-tenant": ["00000000-0000-0000-0000-000000000000"]
      }
    },
    "AdministrationConfig": {
      "Enabled": true,
      "Authority": "https://identity.example.com/",
      "ClientId": "00000000-0000-0000-0000-000000000000",
      "AppAdministrators": {
        "example-tenant": ["00000000-0000-0000-0000-000000000000"]
      }
    },
    "AgentDefinitionPolicyConfig": {
      "AllowedProviderAuthorities": ["https://llm.example.com"],
      "AllowedMcpAuthorities": ["https://tools.example.com"],
      "AllowedServiceTools": [],
      "RequireEvaluationBeforeActivation": true,
      "EvaluationRunTimeoutSeconds": 120
    },
    "AgentRuntimeConfig": {
      "StateSlidingExpirationHours": 168,
      "DefinitionCacheExpirationMinutes": 60,
      "ActiveDefinitionCacheSeconds": 5,
      "ConversationHistoryEnabled": false,
      "MaximumToolInvocationsPerRun": 50,
      "SessionCompaction": {
        "ExampleProvider": {
          "TargetMessageCount": 20,
          "TriggerMessageCount": 30,
          "Notification": "WindowTrimmed"
        }
      },
      "Tenants": {
        "example-tenant": {
          "TimeZoneId": "UTC",
          "CurrentTimeContextEnabled": true,
          "InstructionsDisclosureEnabled": false
        }
      }
    }
  }
}
```

Supply `CasCap__DatabaseConfig__ConnectionString` and
`CasCap__CachingConfig__RemoteCacheConnectionString` through a private provider or secret-backed
environment variables. Redis requires JSON serialization (`CasCap:CachingConfig:RemoteCache:SerializationType`,
already set to `Json` in tracked settings). Provider API keys and logical MCP credentials use the
tenant paths described above and must stay out of definition JSON and tracked settings.

Apply database migrations separately before serving traffic. From a configured Release build:

```powershell
dotnet run --project src\CasCap.App.Server\CasCap.App.Server.csproj --configuration Release --no-launch-profile -- --migrate
```

The migration process still passes authentication/configuration startup validation outside
Development and needs PostgreSQL, but does not require Redis or start the HTTP server. Normal
startup never migrates the database.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.Common.Hosting.AspNetCore` | Shared Serilog, OpenTelemetry, configuration abstractions, and build metadata |
| `CasCap.AgentRuntime.Execution` | Internal project: agent construction, providers, MCP tools, delegation and compaction |
| `CasCap.AgentRuntime.Evaluation` | Internal project: definition evaluation grading, fixtures and recording |
| `CasCap.Common.Caching` | Optional Redis payload/definition caching |
| `CasCap.AgentRuntime.Contracts` | Version 1 request and response DTOs |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Trusted token validation and tenant-claim identity |

Debug builds resolve the CasCap.Common and CasCap.Api.Azure dependencies from adjacent checkouts. Release builds use the centrally versioned NuGet packages.
