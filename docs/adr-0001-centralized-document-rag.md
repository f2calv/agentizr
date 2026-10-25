# ADR 0001: Centralize Multi-Application Document RAG In Agentizr

**Status:** Proposed; not yet accepted or implemented.
**Date:** 2026-10-25.
**Scope:** Blazor document management, Azure Blob/Event Grid ingestion and tenant-isolated RAG.
**Canonical record:** This document. Related issues are historical context or implementation tracking,
not independently maintained copies of this proposal.

## Context And Problem

Applications need agents to answer from reference documents without placing entire PDFs in system
instructions. Two initial use cases are:

- Smart-home assistance grounded in device installation, operation and maintenance manuals.
- German tutoring grounded in authorized Goethe-Institut vocabulary references and sample examination
  material, categorized by language, CEFR level, document type and edition.

Agentizr should manage this capability centrally for multiple applications, rather than each
application maintaining its own ingestion pipeline, embedding provider and vector database.
Applications retain their domain tools and business data.

### Recovered Prior Work

| Record | Verified state on 2026-10-25 | Relationship to this proposal |
| --- | --- | --- |
| [SmartHaus issue #73](https://github.com/f2calv/SmartHaus/issues/73) | Closed on 2026-10-25 with an explicit Agentizr ownership handoff | Historical investigation; closure records ownership, not delivered functionality |
| [SmartHaus PR #5](https://github.com/f2calv/SmartHaus/pull/5) | Closed, not merged | Redis vector-search and PDF-ingestion prototype, not an available production feature |
| [Prototype ingestion at its reviewed revision](https://github.com/f2calv/SmartHaus/blob/ec06cc2521a0f499d1c48264cd35377dba036360/src/CasCap.SmartHaus/Services/Rag/PdfDocumentIngestionService.cs) | PDF extraction, approximate character-based chunking and embedding generation | Useful pipeline evidence, not code to copy unchanged |

Issue #73 explicitly replaced PR #5 as the planning location. No local RAG ADR or matching
RAG/vector/pgvector/embedding issue was found in Agentizr or Tutorizr during this investigation.
This is a search result, not proof that no other discussion or historical branch exists.
Issue #73 was closed at the user's request after this proposal was drafted; PR #5 remains unchanged.

### Current Agentizr Baseline

- [Persistence](persistence.md) and [AgentRuntimeDbContext](../src/CasCap.App.Server/AgentRuntimeDbContext.cs)
  establish PostgreSQL as durable authority and Redis as optional, disposable caching.
- [Host registration](../src/CasCap.App.Server/AppHost.Features.cs) wires PostgreSQL stores,
  tenant context, runtime execution, session serialization and evaluation.
- [AuthenticatedTenantContext](../src/CasCap.App.Server/Services/AuthenticatedTenantContext.cs)
  maps validated caller claims to an allowed tenant. An application is the administration tenant
  boundary; [AdministrationAccessService](../src/CasCap.App.Server/Services/AdministrationAccessService.cs)
  separately authorizes human administrators.
- [AgentDefinition](../src/CasCap.App.Server/Models/AgentDefinition.cs),
  [AgentConfig](../src/CasCap.AgentRuntime.Execution/Models/_AgentConfig.cs) and
  [IAgentRuntimeClient](../src/CasCap.AgentRuntime.Client/Abstractions/IAgentRuntimeClient.cs)
  currently have no dedicated document-ingestion or knowledge-retrieval contract.
- No embedding generator, vector store or `TextSearchProvider` integration was found in the
  inspected Agentizr and adjacent shared-library source. Existing tool configuration alone
  is not a wired document-RAG capability.
- The existing [Blazor administration frontend](administration-frontend.md) manages agent
  definitions, not documents. Document management below is proposed functionality.
- [CasCap.Api.Azure.EventGrid](https://github.com/f2calv/CasCap.Api.Azure/blob/main/src/CasCap.Api.Azure.EventGrid/README.md)
  currently wraps dependencies only: it has no public service, configuration or registration API.
  Reuse and extend this shared library for Event Grid integration rather than assuming a subscriber
  already exists.

## Goals And Constraints

| ID | Requirement |
| --- | --- |
| R1 | Agentizr owns reusable ingestion, indexing, retrieval and lifecycle management; consumers do not access its database directly |
| R2 | Every operation is authorized by tenant and collection; agent execution is additionally restricted to its configured collections |
| R3 | Upload authorized PDFs, observe durable ingestion status, replace/reindex documents and delete them without exposing partial generations |
| R4 | Return bounded relevant passages with document/version/page citations; support German text, CEFR metadata and exact device identifiers |
| R5 | Keep retrieval opt-in per agent and bounded by result count, query size and prompt-token budget; existing agents retain their behavior |
| R6 | Preserve recoverability, idempotency, embedding compatibility and tenant isolation across retries, crashes and multiple replicas |
| R7 | Treat source documents, extracted text, queries and embeddings as sensitive; do not commit source material or export its contents through telemetry |
| R8 | Prove quality using both initial consumer scenarios and the production storage provider before rollout |
| R9 | Provide app-scoped upload, document listing, status, replacement, retry/reindex, protected download and deletion in the Blazor administration frontend |
| R10 | Store original PDFs in private Azure Blob containers; use Event Grid and durable queue delivery to trigger recoverable Agentizr background processing |
| R11 | Keep queue consumption transport-neutral and allow Azure AI Search as an optional index target without replacing the initial pgvector backend |

Initial collections hold application-wide reference material. Learner-private documents and
within-tenant per-user ACLs are out of scope: reject such uploads until a separately designed ACL
model exists. Cross-tenant sharing is not implicit, even for identical public documents.
Conversation-memory indexing, autonomous web crawling, fine-tuning, mandatory OCR and arbitrary
non-document vector workloads are not part of this decision.

## Options Considered

| Option | Benefits | Costs and risks | Assessment |
| --- | --- | --- | --- |
| Keep reference text in prompts or duplicate retrieval in each consumer | No central feature to operate | Prompt growth, inconsistent permissions and repeated ingestion/storage work | Does not meet R1, R3 or R5 |
| PostgreSQL with pgvector | Metadata, lifecycle pointers, chunks and vectors share a transactional authority; exact search and optional ANN indexing | Requires extension-enabled server, provider integration, index tuning and database capacity planning | Recommended initial backend |
| Redis vector search as primary store | Valid vector-search capability; prior prototype exists | Needs search-capable Redis and its own durable-data policy; changes Agentizr's disposable-cache boundary | Not the initial backend; reconsider on measured need |
| Azure AI Search as an optional target | Managed vector/hybrid retrieval; originals also remain available to a Blob indexer | Separate projection consistency, permissions, cost and indexing lifecycle | Design for support; pgvector remains first, no initial dual-write requirement |
| Other dedicated vector service | Specialized scaling and indexing | Another operational, authorization, backup and consistency boundary | Defer until requirements exceed existing backend evidence |
| In-memory vector store | Cheap isolated development and unit fixtures | Not durable or representative of PostgreSQL tenancy, transactions or query plans | Test aid only, not an alternative production mode |

Redis is not rejected because it cannot search vectors. Its
[documented vector search](https://redis.io/docs/latest/develop/ai/search-and-query/vectors/)
supports vector indexes and metadata filters. PostgreSQL is preferred here to avoid an additional
durable authority and to reuse Agentizr's existing persistence boundary.

## Proposed Decision

### D1: Agentizr Owns Tenant-Scoped Knowledge Collections

Introduce an application-owned knowledge service inside the existing Agentizr host. Extend the
existing versioned contracts and typed client for collection/document administration, upload,
ingestion status and search. These are proposed surfaces, not available endpoints or packages.
Do not create a separate service or a generic vector-database framework for this first consumer.

An application selects collections and supplies domain metadata. Agentizr owns extraction,
chunking, embedding, query execution, citations and storage lifecycle. Consumers should not need
pgvector types or framework-specific search-result types.

Extend the existing Blazor frontend with a document area scoped to the selected app and collection.
Administrators can upload PDFs, set language/CEFR/device/edition metadata, list documents, inspect
processing progress and safe errors, retry/reindex, replace versions, download originals and delete
documents. Show uploaded, queued, processing, searchable, failed and deletion/purge states distinctly;
upload completion must never be presented as completed indexing. Surface stale-revision conflicts,
unsupported files, size/quota rejection and unavailable dependencies explicitly.

Keep browser components thin: use
[AdministrationApiClient](../src/CasCap.App.Client/Services/AdministrationApiClient.cs) and server-side
knowledge services under `/admin/api`, with the same domain services behind machine APIs. Start with
bounded server-mediated uploads to private Blob Storage; browser code holds no storage credentials.
Direct browser-to-blob upload, if later needed, requires a separate constrained upload/finalization
contract, not account credentials or arbitrary blob paths.

The service derives tenant identity from the existing trusted context, never from an upload DTO,
model-generated tool argument or query parameter. Human administration continues through the
existing configured administrator assignment, cookie policy and antiforgery protection.
Define separate machine permissions for knowledge read/search and knowledge mutation; the
existing [permissions](../src/CasCap.App.Server/Constants/AgentRuntimePermissions.cs) do not grant
these capabilities today. `agent.execute` must not implicitly authorize document management.
Internal retrieval uses the resolved tenant and agent collection allowlist.

Scope keys, foreign keys, uniqueness checks, status, source downloads and deletes by tenant.
Enforce authorization in database queries, including raw SQL, before returning results; application
post-filtering of globally retrieved results is not the isolation boundary. Development-only
anonymous behavior must not become a production access path.

### D2: PostgreSQL Stores Metadata, Chunks And Embeddings

Use PostgreSQL with pgvector as the first supported production vector backend. Add knowledge
structures alongside the existing `agent_runtime` schema through externally applied additive
migrations. Proposed logical records are:

| Record | Essential information |
| --- | --- |
| Collection | Tenant-qualified identity, authorized purpose, language/metadata policy, embedding profile and active index generation |
| Document/version | Tenant, collection, opaque document ID, immutable source version/hash, protected source reference, title, media type, edition and ingestion status |
| Chunk | Tenant, collection, document version, generation, stable ordinal, extracted text, PDF page range, optional printed-page label and domain metadata |
| Embedding/index generation | Chunk reference, embedding profile, dimension, metric and vector; only compatible generations are searched together |
| Ingestion job | Tenant/document/version, idempotency identity, state, attempt/lease information, progress and safe failure code |

Store original PDFs in private Azure Blob Storage, with separate source containers for each
application tenant initially. Agents within an application bind to logical collections; do not
create a container per agent by default. IaC supplies an explicit account/container-to-tenant mapping,
not a tenant inferred from an untrusted filename or event payload. Actual names/endpoints arrive
through private configuration; examples and fixtures remain synthetic.

Use immutable opaque blob names for source versions, record ETag/version identity and content hash,
disable anonymous access, and authorize downloads through Agentizr. Enable and bound versioning,
soft-delete, lifecycle cleanup and backup retention deliberately; none is an automatic erasure policy.
Keep source containers separate from derived-output and Event Grid dead-letter containers so
processing outputs do not recursively trigger ingestion.

Blob Storage is the authority for original bytes; PostgreSQL remains the authority for document
metadata, permissions, durable jobs and active generations. These are separate transaction
boundaries. The initial searchable projection is pgvector; source storage does not decide the
vector backend.

Start with exact vector search as the correctness baseline. Add HNSW only when corpus-size and
latency measurements justify it. pgvector documents
[exact and approximate indexes](https://github.com/pgvector/pgvector#indexing) and
[filtered ANN behavior](https://github.com/pgvector/pgvector#filtering): ANN filtering can
underfill a result set, so increasing a global candidate count is not an authorization or recall
guarantee. Measure tenant/collection-filtered recall; evaluate iterative scans or partition/index
layout before enabling ANN.

### D3: Embedding Profiles And Retrieval Remain Explicit

An immutable embedding profile identifies provider/model revision, dimensions, distance metric,
normalization and preprocessing/query-document conventions. Chunker/tokenizer versions are
recorded with each generation. Equal dimensions alone do not make two models compatible.
Keep chat and embedding model selection independent; query and stored vectors must use the same
compatible profile. Validate dimension/index compatibility before accepting a generation.

Evaluate `IEmbeddingGenerator` and the
[Microsoft vector-data abstractions](https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/).
Keep any connector behind Agentizr-owned contracts; prove tenant predicates, transactions and
index lifecycle before adopting it. Provider interchangeability is not a promise of equivalent
filtering, hybrid search or migration behavior. The
[pgvector .NET integration](https://github.com/pgvector/pgvector-dotnet#entity-framework-core)
documents EF Core 10 support, but exact package compatibility with this repository still needs a
restore/build spike. Do not import the older issue's package-availability claims as current facts.

For mixed German prose, model numbers and technical terms, evaluate vector-only retrieval against
lexical-plus-vector retrieval using PostgreSQL text search/exact identifier matching. Prefer the
hybrid path if it meets the evaluation gate; do not declare German tokenization or ranking solved
without corpus evidence. Metadata filters include language, document type, model/edition and CEFR
level where present. CEFR levels are metadata, not one database or hardcoded service per level.

A search request carries query text, permitted collection selection and validated metadata filters.
The server clamps result count and token budget. Results carry opaque document/chunk identifiers,
source version, title, PDF page range, excerpt and a documented ranking score/distance meaning.
Use authenticated source/page links, not persistent signed URLs or internal storage coordinates.
Return genuine empty results distinctly from embedding/database failure.

Expose an Agentizr-owned index-target boundary for writing/querying compatible chunk generations.
The initial adapter is pgvector; Azure AI Search is an optional subsequent adapter, not an automatic
fallback or mandatory mirror. Retain tenant/collection/document/version/generation filters and
citations across both. For AI Search, write an inactive generation, verify indexing completion, then
advance PostgreSQL's authoritative pointer; searches select only that active generation and exclude
tombstoned documents. There is no distributed transaction with AI Search, so expose lag/failure and
reconcile retries rather than reporting PostgreSQL commit as proof of remote searchability.

Azure's [Blob indexer](https://learn.microsoft.com/en-us/azure/search/search-how-to-index-azure-blob-storage)
can read source containers, but Blob Storage plus Event Grid does not automatically populate a
vector index. Prefer Agentizr-owned chunking and explicit index writes for parity. A future
Search-managed indexer/skillset/vectorization route must first prove tenant filtering, citations,
generation activation and deletion semantics; do not run competing ingestion owners implicitly.

### D4: Upload And Indexing Are A Recoverable Workflow

Use the initial delivery path:

`Blazor/operator upload -> private Blob -> Event Grid -> Azure Storage Queue -> Agentizr inbox -> PostgreSQL job -> background processing -> pgvector`

An operator may upload the first authorized test PDFs directly into the two pre-provisioned source
containers before the UI exists. Only configured containers are eligible for discovery; ingestion
validates size, type, provenance and metadata just as it does for UI uploads. Unknown or invalid
source mappings must not create a guessed tenant or collection.

1. The separate infrastructure work provisions storage containers, a storage system topic/event
   subscriptions, queue destination, identities/RBAC, network access, bounded retry/retention and
   dead-letter storage. Filter completed source writes and source deletions to the allowed containers
   using the [Blob event model](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-event-overview).
   Confirm final-write event/API behavior for the upload method; never index partially uploaded data.
2. An Agentizr background receiver normalizes and validates the configured Event Grid envelope,
   topic, event type, account/container and blob identity. Events are signals, not authorization to
   fetch arbitrary URLs. Resolve the blob through trusted configuration and verify its current
   identity. Event Grid is
   [at-least-once and unordered](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry),
   so handle duplicate, stale and out-of-order create/delete notifications.
3. Commit an idempotent inbox receipt and durable job/tombstone in PostgreSQL before acknowledging
   the queue message. A crash after commit but before acknowledgement causes safe redelivery.
   Do not keep a transport message leased for the entire PDF/embedding operation. Retry temporary
   admission failures; quarantine poison envelopes durably before acknowledging them.
4. Reconcile allowed source containers against registered documents/jobs to recover lost, expired or
   pre-subscription events and incomplete upload acceptance. Do not reactivate tombstoned versions
   during reconciliation or replay; a later restore requires an explicit authorized new version.

The document-processing workflow is:

1. Authorize and bound upload bytes/pages, validate actual PDF content, quarantine untrusted input
   and run extraction with resource/time limits. Store provenance and the uploader's rights basis.
   Do not fetch arbitrary caller-provided URLs.
2. Register upload intent, durably store source bytes, then finalize the document version as awaiting
   an ingestion signal. Event reception may race finalization: defer/retry or reconcile against the
   intent rather than discard the event. Acknowledging upload means durable acceptance, not
   searchable completion. Cross-store writes are not atomic; reconcile incomplete acceptance safely.
3. Workers claim durable jobs with expiring leases and fenced ownership. Use scoped database units
   of work; do not hold a database transaction open during parsing or remote embedding calls.
   An in-memory queue may wake workers but must not be the only job record.
4. Extract page-aware text, detect unreadable/scanned/encrypted documents explicitly and chunk with
   a real tokenizer and bounded overlap. Preserve headings and vocabulary-entry boundaries where
   possible. Unsupported extraction is a visible failure, not a successful empty index.
5. Generate and validate embeddings in bounded batches, then write an inactive generation.
   Idempotency includes tenant, collection, source version/hash and pipeline/profile version;
   filenames are not document identity. Duplicate retries must not create duplicate active chunks.
6. Atomically activate only a complete generation in PostgreSQL using an expected revision.
   Document replacement advances its active version; a collection-wide profile change switches
   the complete collection generation. Stale workers cannot activate over newer work or a delete.
   A failed rebuild leaves the prior active generation searchable and exposes the failure.
7. API deletion first commits a tombstone/revocation so new searches and source reads cannot select
   the document. Operator-side blob deletion is eventually observed by events/reconciliation, not
   synchronously revoked in PostgreSQL; controlled production deletion should use Agentizr.
   Cleanup removes source bytes, text, embeddings, optional remote indexes and jobs under the policy.
   Previously authorized in-flight responses cannot be recalled. Report purge progress separately
   from revocation and account for backups and already generated answer text.

The prototype's delete-before-reingest behavior, filename-derived identity, approximate token
counting and document-name logging must not be carried into this design.

### D4a: Queue Transport Is Replaceable Without Weakening Delivery

Define a narrow application-owned queue receiver interface (illustrative name:
`IDocumentIngestionEventQueue`) with bounded receive, opaque receipt/lease, lease renewal,
acknowledgement, delayed retry/release and durable poison disposition. Expose cancellation and
recognized failure states; keep Azure/Redis SDK message types out of the ingestion use case.
Select adapters at composition time, not through model input.

The first adapter uses
[Azure Storage Queue as the Event Grid destination](https://learn.microsoft.com/en-us/azure/event-grid/handler-storage-queues).
Verify Event Grid schema and message encoding explicitly. Extend
[CasCap.Api.Azure.EventGrid](https://github.com/f2calv/CasCap.Api.Azure/blob/main/src/CasCap.Api.Azure.EventGrid/README.md)
with reusable envelope validation/mapping as required. Reuse/extend the shared Azure storage package
for queue transport, but its current
[AzQueueStorageBase](https://github.com/f2calv/CasCap.Api.Azure/blob/main/src/CasCap.Api.Azure.Storage/Services/Base/AzQueueStorageBase.cs)
deletes received messages before the caller commits application work and logs malformed bodies.
It cannot directly implement durable inbox admission; add an explicit receive/acknowledge path
without copying that behavior into Agentizr.

A future in-cluster Redis adapter must pass the same delivery contract. A destructive list pop
alone is insufficient; use a recoverable processing-list pattern or streams/consumer groups with
pending-item recovery, bounded retries and durable poison handling. PostgreSQL jobs remain durable
authority, regardless of the queue adapter.

Event Grid does not gain a direct Redis destination by changing a .NET interface. A Redis deployment
also needs a supported ingress/bridge, initially Azure Queue to Redis if appropriate, that acknowledges
upstream only after durable downstream admission. Define Redis persistence/replay and queue-cutover
drain/deduplication before enabling that alternative. Implement only Azure Queue initially, with
shared contract tests ready for another adapter.

Bound queue polling batches, active jobs, extraction memory and embedding concurrency separately.
On shutdown stop receiving, finish or release uncommitted admissions, then drain processing to a
deadline; leave unfinished PostgreSQL jobs recoverable through lease expiry. Supervise receiver and
worker failures explicitly instead of silently stopping processing while the API remains healthy.

### D5: Retrieval Is An Opt-In Runtime Capability

Extend immutable versioned agent definitions with validated tenant-local collection bindings,
retrieval mode and budgets. Definition publication/activation remains separate from mutable corpus
updates. Record the definition version and selected corpus generation for reproducible diagnostics;
rolling back a definition does not automatically roll back the corpus.

Evaluate the framework's
[`TextSearchProvider`](https://learn.microsoft.com/en-us/agent-framework/agents/rag#using-textsearchprovider)
as the adapter for both before-invocation and model-invoked search. Use on-demand search initially
for mixed control/chat agents; require before-invocation retrieval for reference-only tutoring or
manual-answering agents when evaluation shows the model skips necessary searches. Agents without
bindings do not retrieve. A caller may narrow permitted collections but never broaden them.

Wire execution, delegation, streaming, evaluation and session reconstruction consistently through
the existing [executor](../src/CasCap.App.Server/Services/CommonAgentExecutor.cs),
[session codec](../src/CasCap.App.Server/Services/CommonAgentSessionCodec.cs) and
[evaluation factory](../src/CasCap.App.Server/Services/EvaluationAgentFactory.cs).
Validate the API against the pinned framework version rather than assuming current documentation
matches the installed package.

Retrieved passages are untrusted evidence, never instructions or permission to execute tools.
Require source attribution for grounded claims and abstention/clear qualification when no source
supports an answer. Device-control authorization and safety checks remain outside document RAG.

Do not persist injected passages as ordinary conversation messages. Exercise serialization,
multi-turn search-provider state, [compaction](session-compaction.md), snapshots and the optional
[debugging archive](persistence.md#conversation-archive): stripping tool results on a later turn
does not prove passages were never stored. Generated answers may themselves quote source content;
document that separate retention exposure.

## Consequences

- Consumers gain one upload/search lifecycle and consistent tenant isolation. Agentizr also gains
  responsibility for the document UI, PDF security, event/inbox handling, ingestion workers,
  quotas, storage growth and relevance quality.
- Extension installation requires an extension-capable PostgreSQL image and a privileged external
  migration path. Verify both against the actual target before rollout; current deployed extension
  availability was not checked. Runtime database roles should not gain schema privileges.
- PostgreSQL loss/unavailability prevents durable acceptance and knowledge search. Report the
  failure explicitly; a reference-required agent must not silently answer as though grounded.
  An optional retrieval agent may continue only with an explicit unavailable-context indication.
- Redis remains optional and unnecessary for the first release. Any later result cache must include
  tenant, authorized collection set, generation/profile and filters, and revalidate revocation
  before serving. Clearing Redis must not change committed knowledge or its availability.
- Embeddings and extracted text are sensitive derivatives. Keep document names, queries, excerpts,
  vectors and learner content out of logs/metrics/traces. Use safe status codes and aggregate
  measurements. External embedding providers require explicit policy approval for document/query
  egress; do not infer approval from an existing chat-provider configuration.
- Retain PDFs and licensed material only in authorized private storage at runtime, consistent with
  [Tutorizr's content boundary](https://github.com/f2calv/tutorizr/blob/main/.github/copilot-instructions.md#content).
  Public download availability does not grant redistribution rights. Do not commit real PDFs or
  copy protected word lists/exam papers into fixtures.
- RAG is not proof that a word belongs to an allowed vocabulary list, and top-k retrieval cannot
  enumerate a complete level's vocabulary. If exact membership or exhaustive lesson coverage is
  required, add a separately validated structured vocabulary import/query contract with provenance;
  keep learner progress in the consumer's domain store. No completeness claim is made here.
- Back up source bytes and PostgreSQL metadata coherently. Retained generation/profile metadata
  enables reindexing, but re-embedding depends on model availability and may not reproduce identical
  vectors. Restore validation must cover both stores and pending jobs, not just vector row counts.
- Azure resource provisioning stays in a separate infrastructure workstream, before application
  implementation. Verify the reported existing Azure AI Search service and its supported features
  there; no live deployment or Event Grid subscription readiness was verified for this ADR.
- Defer agent/application bootstrap automation until manual onboarding is proven. Later choose a
  bootstrap skill or setup wizard that requests IaC-managed provisioning of containers, event
  bindings and permissions. Ordinary agent creation must not silently create resources or grant
  the runtime broad infrastructure permissions. This is not required for the initial two consumers.

## Validation And Delivery

Delivery items below are planned and unchecked; no ingestion, model benchmark, database test or rollout
was executed while writing this ADR. Numerical targets are proposed acceptance gates, not results.

### Phase 0: Provision Infrastructure And Stage Source Documents First

The separate IaC workstream owns the following handoff; deployment is not performed by this ADR:

- [ ] Verify existing Azure AI Search inventory/capabilities rather than assume deployed state.
  Provision private source containers for the two initial consumers, and separate delivery
  dead-letter storage, with explicit tenant/collection bindings and retention/versioning policy.
- [ ] Provision storage Event Grid system-topic subscriptions for final writes/deletes, container
  filters, Azure Storage Queue, retry/TTL/dead-letter settings, and least-privilege managed/workload
  identity access. Verify network/firewall compatibility for delivery and worker access (R10).
- [ ] Supply account/container/queue identifiers and authentication configuration through private
  operator configuration; retain no real infrastructure coordinates in this public ADR.
- [ ] Upload one or two authorized test PDFs per initial application container; verify blob
  persistence and Event Grid queue delivery. Keep messages until Agentizr is ready, using explicit
  retention, reconciliation or replay if the implementation starts after their expiry.
- [ ] Record delivery/dead-letter recovery evidence independently of application processing. A
  staged PDF or queued event does not yet prove document ingestion, indexing or agent answers.

### Phase 1: Accept The Decision And Prove Compatibility

- [ ] Accept or amend this ADR; keep acceptance separate from implementation progress.
- [ ] Select the multilingual embedding profile, PDF/tokenizer libraries, upload/download limits,
  rights/retention policy, quotas and target hardware before implementing durable ingestion (R3, R7).
- [ ] Verify extension/image availability, pgvector/provider/package compatibility and framework
  search/history APIs using the repository's pinned dependencies (R6).
- [ ] Build an authorized evaluation set of at least 40 questions, at least 20 per initial use case,
  including German inflections/umlauts, device identifiers, edition/CEFR filtering and unanswerable
  questions. Use synthetic or appropriately licensed CI documents (R4, R8).

### Phase 2: Deliver Central Upload, Storage And Search

- [ ] Prove authenticated upload/status/search/source/delete through versioned HTTP and typed-client
  surfaces against isolated PostgreSQL with pgvector, not an in-memory substitute (R1-R4).
- [ ] Deliver Blazor document management through the existing administration API/client: app-scoped
  upload/list/status/metadata/download/replace/retry/reindex/delete, antiforgery enforcement,
  stale-revision conflicts and distinct accepted/processing/searchable/failure states (R9).
- [ ] Extend shared Azure Event Grid/storage helpers and prove the transport-neutral receive/lease/
  acknowledge contract. Exercise duplicate/unordered events, encoding, outage, poison handling,
  inbox-commit-before-acknowledge and reconciliation of pre-existing blobs (R6, R10, R11).
- [ ] Verify zero cross-tenant or unauthorized-collection results across read/write/download,
  guessed IDs, filters, duplicate uploads, retries and delegation (R2).
- [ ] Kill/restart workers during upload, embedding and activation; prove durable recovery,
  idempotent chunks, fencing and no partially active generation. Exercise replacement/delete races,
  profile reindexing and source/database restore (R3, R6).
- [ ] Achieve relevant-source recall@5 of at least 90% on answerable labeled questions in each use
  case. Compare vector and hybrid retrieval. If enabling ANN, also achieve at least 95% overlap with
  the exact-search top-5 baseline under representative tenant/metadata filters (R4, R8).
- [ ] Record corpus size, hardware, model/profile, concurrency, ingestion throughput, storage/index
  size and warm/cold p50/p95 search latency including query embeddings. Agree a numeric latency SLO
  from those results before production; do not choose a backend from an unmeasured speed claim (R8).

### Phase 3: Integrate Agents And Onboard Consumers

- [ ] Agents in both initial scenarios answer questions requiring indexed evidence, cite the correct
  document/version/page and abstain on unsupported questions. Measure answer quality and prompt
  tokens/latency against the no-retrieval baseline; reject irrelevant context stuffing (R4, R5).
- [ ] Verify opt-in definitions, evaluation, delegation, streaming, snapshots and compaction.
  Multi-turn persisted state/archive checks show injected passages do not accumulate; unchanged
  agents retain behavior (R5, R7).
- [ ] Verify explicit unavailable/failed/unsupported states, quota rejection, revocation after delete,
  protected citation links and no document/query content in telemetry (R2, R3, R7).
- [ ] Add targeted provider-backed and runtime behavior tests to the existing runners and CI with
  explicit setup/opt-in handling. Record actual execution evidence, not just test-file existence (R8).
- [ ] Apply additive migrations externally, onboard one collection per consumer, then expand.
  Roll back application integration by disabling retrieval without dropping source data/schema.
  Retain only policy-permitted previous generations for explicit corpus rollback (R3, R6).
- [ ] Keep Agentizr implementation on the current feature branch. Use
  [deploy.ps1](../deploy.ps1) for initial Debug deployments with the needed adjacent shared-library
  `ProjectReference` paths, including Azure library changes. Verify the Debug image's actual
  dependencies and live blob-to-answer flow; Release package validation is a later distinct gate.
  This plan does not authorize deployment, commits, pushes or a new remote workstream by itself.

### Later Gates: Optional Search Target And Bootstrap

- [ ] Before enabling Azure AI Search, prove the same tenant/citation/generation/delete contract
  with provider-backed tests, remote indexing lag/failure recovery and a measured retrieval
  comparison. No unverified automatic fallback or pgvector/AI Search dual-write promise (R11).
- [ ] After manual onboarding is repeatable, choose bootstrap skill versus setup wizard and define
  the authorized IaC provisioning boundary. Until then, provision new containers/bindings explicitly.

## Open Questions And Reconsideration

- Which Blob retention/versioning, upload/download limits and backup/purge ownership fit the first
  deployment, including incomplete uploads and restoration of deleted material?
- Which Event Grid/queue retention, poison replay and reconciliation settings cover the initial
  Debug iteration window? Which ingress/bridge is appropriate if Redis later replaces Queue Storage?
- Which authorized PDFs/editions and embedding provider/model meet German and technical retrieval
  quality? Which materials require OCR, structured vocabulary extraction or additional rights?
- What ingestion/search quotas, retention windows and latency SLO fit measured corpus growth and
  target hardware? These must be resolved before production acceptance.
- Does a consumer need user-private documents or licensed cross-application sharing? Design explicit
  ACLs/licensing and revocation before admitting either, rather than weakening tenant isolation.
- Evaluate the optional Azure AI Search target, another vector service or Redis search if PostgreSQL contention,
  filtered recall or latency fails the agreed gates after reasonable tuning. A change needs a
  follow-up ADR covering durable authority, migration, rollback and operational cost.

## Evidence And Related Decisions

The links beside each claim are the evidence. Historical discussion is not current implementation.
Official documentation was consulted on 2026-10-25; exact version compatibility remains a Phase 1
gate. This ADR is saved locally. The SmartHaus ownership-handoff closure is verified; infrastructure,
application implementation, document ingestion and deployment remain unverified.
