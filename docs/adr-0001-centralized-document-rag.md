# ADR 0001: Centralize Multi-Application Document RAG In Agentizr

**Status:** Proposed; Debug admission is deployed with ingestion disabled; job processing and PDF preparation are local-only.
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
- The adjacent `CasCap.Api.Azure.EventGrid` checkout now has a reusable `BlobStorageEventReader`
  and normalized `BlobStorageEvent` model. These local APIs are not yet published packages.
  There is still no shared subscriber or provisioning API; Agentizr owns queue admission.
- [Role selection](../src/CasCap.App.Server/Models/_FeatureConfig.cs) and
  [host composition](../src/CasCap.App.Server/AppHost.cs) now separate API, ingestion and migration
  responsibilities. This is one application host, not three independently built services.

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

Chunk size is constrained by the embedding model's tokenizer/input limit and evaluated against
retrieval quality and the answering model's context budget, not by GPU location alone. Different
answering models need not imply different indexes. The local preparation API accepts per-call
token/overlap settings and captures an immutable preparation profile; a changed profile requires a
new generation rather than mixing old and new chunks. Its current `cl100k_base` tokenizer is a PoC
choice, not a compatibility claim for an unspecified local or hosted embedding model.

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

Use the application-owned
[`IDocumentIngestionEventQueue`](../src/CasCap.App.Server/Abstractions/IDocumentIngestionEventQueue.cs)
with bounded receive, opaque receipt/lease, lease renewal,
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
It cannot directly implement durable inbox admission. The local
[Azure adapter](../src/CasCap.App.Server/Services/AzureDocumentIngestionQueue.cs) therefore uses SDK
receive/update/delete operations explicitly, via the shared storage dependency. A reusable leased
consumer can move into that library when another consumer needs the same contract; do not change
the existing destructive helper's behavior implicitly.

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

### D4b: Modular Roles And Elected Ownership

The startup-only setting `CasCap:FeatureConfig:EnabledFeatures` selects comma-separated roles.
Names are case-insensitive, whitespace is trimmed and duplicates collapse. Empty, unknown and
incompatible selections fail explicitly.

| Selection | Dependencies and surfaces | Lifecycle |
| --- | --- | --- |
| `agentizr` (default) | Existing runtime/admin/frontend/MCP gates; PostgreSQL required outside Development, Redis optional | API daemon; does not consume document notifications |
| `ingestion` | PostgreSQL, shared Redis election and Azure queues; only `/status` and `/healthz`, no business API/frontend/MCP registration | Elected serial admission and gated processing daemon; standby keeps serving probes |
| `agentizr,ingestion` | Union of both roles, with shared persistence/caching registration | API and elected ingestion together |
| `migrate` | PostgreSQL and migrator only; no API/ingestion registration or HTTP listener | Finite external schema job; cannot combine with daemon roles |

The existing `--migrate` invocation selects migration-only behavior. The same application artifact
serves every role; no separate entrypoint, Dockerfile or worker image is introduced.
The ingestion PoC currently requires Debug sibling-project references. Release explicitly rejects
that selection until the new shared APIs are published and its package path is validated.

The [ingestion feature](../src/CasCap.App.Server/Services/DocumentIngestionFeature.cs) delegates lease
acquisition, loss, awaited cleanup and reacquisition to the shared `ILeadershipRunner`, not a custom
Redlock loop. Use a stable installation-qualified `LeadershipResource` identical on all ingestion
replicas, never a pod-specific name. `CachingConfig.RemoteCache.IsEnabled`,
`RemoteCacheConnectionString` and `DistributedLockingEnabled` are required; use the shared
`CachingConfig.Redlock` timing/topology contract and a suitable non-failover-managed lock authority.
Leadership loss cancels receive, database work, acknowledgement and idle waits through the ownership
token, including processing attempts. Only the owner's callback starts ingestion. This is coordination, not exactly-once execution
or downstream fencing.

The [inbox](../src/CasCap.App.Server/Services/DocumentIngestionInboxService.cs) uses a PostgreSQL
transaction-scoped advisory lock to serialize all admissions, claims and outcomes in this PoC, plus deterministic event
receipts and tenant/collection-qualified document identity. This deliberately favors simplicity over
parallel throughput. Per-blob hexadecimal sequencers reject stale notifications; deleted immutable
blob names cannot be resurrected. New versions require new opaque blob names. Receipt conflicts are
quarantined rather than silently reassigned. Treat source mapping/leadership-resource changes as
controlled migrations with queue drain/replay, not mutable agent settings.

`knowledge_documents` records the latest admitted source state.
`document_ingestion_jobs` combines event receipts and processing intents. `Queued` means durably
awaiting a processor, **not extracted, embedded or searchable**; `Deleted` is a tombstone and
`Ignored` records stale/superseded work.

The local
[job lifecycle](../src/CasCap.App.Server/Services/DocumentIngestionJobService.cs) claims one current
source revision transactionally, with a unique attempt ID, persisted attempt count and deadline.
PostgreSQL supplies deadline time after acquiring the same admission lock; SQLite tests use an
injected clock. `Processing` transitions to `Completed`, `RetryPending` or `Failed`. Three attempts
are allowed, including abandoned claims. Known retryable failures wait a persisted 30 seconds.
An enabled polling owner moves expired attempts to a retry due 30 seconds after recovery, or to
terminal failure at the attempt limit; there is no exact failover-time promise. Unknown faults
escape to host supervision rather than being silently classified as transient.

[Processing orchestration](../src/CasCap.App.Server/Services/DocumentIngestionProcessingService.cs)
is explicitly gated by `ProcessingEnabled=false`. Startup rejects enabling it without a real
[`IDocumentProcessor`](../src/CasCap.App.Server/Abstractions/IDocumentProcessor.cs); no production
processor or no-op substitute is registered yet. `ProcessingTimeoutSeconds=300` (range 30-900)
cancels cooperative attempts. Shutdown/ownership loss aborts rather than drains the active attempt,
awaits cleanup and leaves its claim for recovery, without recording a cancellation as success or
permanent failure. An uncooperative processor can exceed the local cancellation budget; it is
never detached. The database still rejects completion after the durable deadline.

Completion/retry requires the current unexpired claim and latest nondeleted source revision under
the admission lock. Delete/newer-create admission revokes active and retry claims. This protects
job bookkeeping, **not external effects or a future generation activation**: a real processor must
stage idempotent outputs and fence the active-generation transaction independently.
`Completed` is processor completion, not searchability; document state remains admission-only.
No PDF bytes are downloaded in this slice.
The additive processing migration must be externally applied before rollout. Keep processing off
until all ingestion replicas use this contract, the real processor is wired and provider-backed
race tests pass; the existing admission-only deployment does not include this local lifecycle.

### PoC Limits And Deliberately Deferred Polishing

This may remain a hobby PoC. Keep the first implementation serial and explicit rather than adding
a generalized scheduler or provider framework. The following are not delivered guarantees:

| Deferred work | Current compromise and revisit trigger |
| --- | --- |
| Provider-backed delivery/race tests | SQLite tests prove state transitions, not PostgreSQL advisory locks or cross-pod election. Prove those against isolated providers before a multi-pod ingestion rollout |
| Full role-composition acceptance | Parsing, fake-runner ownership and existing API tests are local checks; worker-only/combined HTTP surfaces and migration process exit still need isolated host acceptance before deployment |
| Transport recovery and progress readiness | Job attempts now have a persisted bounded retry policy, but transport retries remain Azure SDK-owned. Exhausted transport/database failures propagate to host supervision; unacknowledged messages return after visibility expiry. Validate restart behavior and add queue-age/progress health before unattended operation |
| Long admissions and poison lifecycle | One short admission runs at a time, without a lease-renew loop; expiry can redeliver safely through inbox deduplication. Poison enqueue precedes source acknowledgement, so duplicates are possible. Add renewal, retention/replay tooling and limits when latency or poison volume warrants them |
| Reconciliation | Events before subscription, expired deliveries and unfinalized uploads are not discovered automatically. Stage/replay test events explicitly; add container reconciliation before relying on loss recovery |
| Source validation and indexing | Offline PDF preparation and a shared-library conditional download adapter exist; envelope/source/extension checks still do not validate a live Blob or its rights. Connect them to the processor, prove live ETag/identity behavior, then add embedding profiles and pgvector before claiming RAG |
| PDF isolation, OCR and layout quality | Synchronous parsing/extraction cannot be interrupted mid-call; byte/page/text/chunk caps are not a hard CPU/memory sandbox. Isolate untrusted parsing before accepting hostile uploads. No-text pages are reported, not distinguished as blank versus scanned; add OCR/review and section/vocabulary-aware splitting when corpus evaluation demonstrates a need |
| Real processing and generation activation | Claims/retries/completion are implemented behind a disabled gate and exercised with test processors. No PDF processor, idempotent output staging or fenced active-generation switch exists. Add these before enabling processing; claim identity alone cannot fence external effects |
| Scheduling throughput and deadlines | One serial loop alternates admission with one bounded attempt, so long processing delays further admissions. A single transaction lock and bulk expiry recovery favor simplicity over scale. Split supervised admission/processing loops, bound recovery batches and measure fairness/progress if corpus size or admission latency warrants it |
| Management and cleanup | Upload/status/download/replace/delete UI/API, quotas, purge/backup retention and source/index cleanup remain future steps before broader use |
| Optional providers and onboarding | Add AI Search parity, a Redis transport/bridge or bootstrap wizard only after the initial manual pgvector path is proven |

### D5: Retrieval Is An Opt-In Runtime Capability

Extend immutable versioned agent definitions with validated tenant-local collection bindings,
retrieval mode and budgets. Definition publication/activation remains separate from mutable corpus
updates. Record the definition version and selected corpus generation for reproducible diagnostics;
rolling back a definition does not automatically roll back the corpus.

Evaluate the framework's
[`TextSearchProvider`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.textsearchprovider)
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

Local XML API inspection confirms Agentizr's pinned `Microsoft.Agents.AI` 1.26.0 already exposes
`TextSearchProvider`, a cancellation-aware search delegate, `SourceName`/`SourceLink`/`Text`
results and both search behaviors. Agentizr's future tenant-authorized retrieval service supplies
that delegate; the framework does not own tenant authorization or the durable ingestion pipeline.
No framework retrieval hook is wired by the current job-lifecycle slice.

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
  management path. Read-only follow-up verified successful declarative activation, an installed
  `vector` extension in `public`, and the expected result from a vector-distance calculation.
  Provider/package compatibility and application retrieval behavior remain separate gates;
  runtime database roles should not gain schema privileges.
- PostgreSQL loss/unavailability prevents durable acceptance and knowledge search. Report the
  failure explicitly; a reference-required agent must not silently answer as though grounded.
  An optional retrieval agent may continue only with an explicit unavailable-context indication.
- Redis remains optional for API-only deployments, but is required for ingestion leadership.
  Its loss prevents elected admission; it does not erase PostgreSQL receipts/jobs or Blob originals.
  Any later result cache must include
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
- Azure resource provisioning stays in a separate infrastructure workstream, before live ingestion
  validation. Application foundations can progress independently. The sanitized Phase 0 status
  below records verified storage/Search provisioning separately from the still-unverified
  ingestion transport and application deployment.
- Defer agent/application bootstrap automation until manual onboarding is proven. Later choose a
  bootstrap skill or setup wizard that requests IaC-managed provisioning of containers, event
  bindings and permissions. Ordinary agent creation must not silently create resources or grant
  the runtime broad infrastructure permissions. This is not required for the initial two consumers.

## Validation And Delivery

The admission-only local slice and infrastructure prerequisites are tracked separately below.
Completed prerequisites do not establish broader RAG acceptance. Numerical quality targets are
proposed gates, not measured results.

### Local Admission Foundation

- [x] Implement role parsing, elected serial admission, trusted source validation, commit-before-ack
  and relational duplicate/stale/delete/tenant tests. Focused credential-free execution is recorded
  in the [test README](../src/CasCap.App.Server.Tests/README.md#document-admission-poc).
- [x] Scaffold the additive
  [DocumentIngestionAdmission migration](../src/CasCap.App.Server/Migrations/20261025123301_DocumentIngestionAdmission.cs)
  for source state and event/job receipts. Rebuilt EF tooling reports no pending model changes;
  reviewed offline SQL adds only the two tables, their constraints/indexes and migration history.
  It does not install pgvector or store chunks/vectors. External application is still required.
- [x] Build the Agentizr Debug solution and the shared Event Grid project for .NET 8/9/10 with
  zero warnings/errors. This validates local source references, not the Release package path,
  container build or deployment.
- [ ] Prove the actual Azure adapter's encoding, lease/poison behavior and end-to-end delivery.
- [ ] Prove PostgreSQL concurrency, real Redis leadership loss and worker/combined host behavior.

### Phase 0: Provision Infrastructure And Stage Source Documents First

The separate IaC workstream owns the following handoff; deployment is not performed by this ADR:

- [x] Verify the optional Azure AI Search inventory/capabilities. The infrastructure workstream
  verified Free-tier provisioning, authenticated service-statistics access, an empty index/document
  inventory and the configured Search role assignments. Free has a 50 MiB storage limit and no
  outbound managed identity; this does not select Search as the backend or prove indexing.
- [x] Provision optional Document Intelligence access for a bounded layout-to-Markdown evaluation.
  On 2026-10-26 the approved infrastructure apply created a Standard S0 account in the pay-as-you-go
  subscription and granted the existing application service principal account-scoped
  `Cognitive Services User`. Key authentication is disabled; the custom-subdomain HTTPS endpoint
  rejects anonymous requests with HTTP 401. Live provisioning, the role assignment and a no-change
  follow-up Terraform plan are verified. That infrastructure apply did not submit documents or
  enable ingestion.
- [x] Verify Document Intelligence data-plane access using the application's existing identity and
  compare the approved source-pinned pages in a bounded test-only benchmark. On 2026-10-26 two
  submissions analyzed exactly six physical pages and returned valid pinned-model Markdown/page
  spans; private comparisons and resumable receipts were saved. See the
  [benchmark contract and measured results](../src/CasCap.App.Server.Tests/README.md#charged-document-intelligence-benchmark).
- [ ] Accept managed extraction semantics and implement the production preparation adapter before
  chunking or embedding. Benchmark contract success is not semantic-quality acceptance.
  The reported seasons passage is on physical PDF page 8, outside this six-page budget; it was not
  submitted or verified. Additional charged pages need separate approval.
- [ ] Resolve the Azure OpenAI data-plane access blocker and then complete the separately approved
  PDF-only/hybrid comparison. On 2026-10-27 the initial generation POST returned HTTP 400
  `SubscriptionNotRegistered`; read-only model listing with the documented v1 audience returned
  the same category despite control-plane provider registration being `Registered`.
  The owner selected mirrored consumption-based AI services across both subscriptions; provisioning
  and access verification remain with the separate infrastructure workstream. No OpenAI candidate was
  generated. See the [OpenAI access handoff](#openai-access-handoff-2026-10-27).
- [x] Prepare shared mirrored-service configuration and review the alternative-subscription plan.
  The reviewed plan is additive, retains the working Document Intelligence account and existing
  transport, keeps Search Free and OpenAI deployments consumption-based, and adds no metric alerts.
  Fifteen mocked Terraform cases and static configuration checks passed; this is not live
  provisioning or inference-access evidence.
- [ ] Obtain separate apply approval, provision the missing counterparts and verify application
  access and convergence. The original-subscription live plan is blocked by its backend reporting
  `ReadOnlyDisabledSubscription`; do not move state or replace existing accounts to bypass it.
- [x] Provision dedicated Standard GPv2 Hot/LRS storage in the durable-data subscription through
  the existing storage module. Live settings disable anonymous Blob access and shared-key
  authentication, require HTTPS/TLS 1.2, and preserve the existing runtime principal's inherited
  Blob access. An unauthenticated Blob request returned HTTP 401; the post-apply Terraform plan
  reported no changes using the applied deployment metadata.
- [x] Provision private source containers for the two initial consumers and separate delivery
  dead-letter Blob storage. The local infrastructure apply created three private containers;
  live IDs/endpoints match the outputs, unauthenticated container requests return HTTP 401,
  and a follow-up Terraform plan reports no changes.
- [x] Provision distinct ingestion and application-poison queues. Both exist and their live ARM
  IDs and credential-free endpoints match the infrastructure outputs. This does not establish
  producer TTL, runtime access or event delivery.
- [ ] Complete approved tenant/collection bindings and producer retention configuration before
  enabling ingestion. The account does not enable Blob/container soft delete or Blob versioning,
  and Azure reports no automatic lifecycle policy.
- [x] Provision storage Event Grid system-topic subscriptions for final writes/deletes, container
  filters, queue destinations, retry/TTL/dead-letter settings, and least-privilege Entra access.
  Configure Event Grid's managed identity separately from the worker's existing identity path.
  On 2026-10-26 the approved Terraform apply provisioned all four subscriptions and a follow-up
  plan reported no changes. Controlled probes isolated destination-authorization failures to
  delivery-role scope; Event Grid's sender and dead-letter writer now use dedicated-account scope.
  The worker's queue grants remain narrowly scoped. Synthetic create/delete events from both
  sources reached the ingestion queue, proving the Event Grid network/delivery path (R10).
- [ ] Verify the worker identity, DNS/network access and queue lease/poison behavior from the
  intended runtime; successful Event Grid delivery does not prove pod or application access.
- [ ] Supply account/container/queue identifiers and authentication configuration through private
  operator configuration; retain no real infrastructure coordinates in this public ADR.
- [ ] Upload one or two authorized test PDFs per initial application container; verify blob
  persistence and Event Grid queue delivery. Keep messages until Agentizr is ready, using explicit
  retention, reconciliation or replay if the implementation starts after their expiry.
- [x] Record synthetic delivery independently of application processing. Four notifications were
  inspected without dequeueing: each was a single EventGridSchema JSON object encoded as Base64
  UTF-8, with the correct storage-account topic, source subject/URL and `PutBlob`/`DeleteBlob` API.
  All had non-expiring queue retention (expiration year 9999) and dequeue count zero. Synthetic
  blobs and temporary operator permissions were removed; the four test messages remain queued.
- [x] Prove isolated dead-letter recovery and operator replay. On 2026-10-26 a deleted temporary
  destination forced `NotFound`; a one-attempt test subscription dead-lettered the synthetic event
  with `MaxDeliveryAttemptsExceeded`, observed after 394 seconds. The source ETag/content length
  were preserved. Replay into the restored isolated queue retained the original event ID and
  core fields, used single-object Base64 UTF-8 with explicit TTL `-1`, and was verified by peeking
  (expiration year 9999, dequeue count zero). Temporary resources/grants were removed and absence
  verified; the four live routes and existing ingestion messages were untouched. This does not
  prove application admission, deduplication, poison handling, document ingestion or agent answers.

The operator reports both source accounts online and one example PDF staged for each initial
consumer. The application workstream has now verified authenticated conditional reads and bounded
preparation against both configured source containers using the local runtime certificate and
standard Key Vault provider. It used synthetic ownership bindings only in the test process.
This does not establish pod access, production ownership bindings, their PDF notification delivery
or end-to-end database staging; those gates remain open.

#### Exact IaC Handoff

**Readiness on 2026-10-27: partial; not ready to enable live ingestion.** Account provisioning,
private source/dead-letter containers, ingestion/poison queues and optional Search verification
are complete, as is optional Document Intelligence account provisioning and scoped RBAC.
Resource IDs and credential-free container/queue URIs are verified in private
infrastructure outputs. The Event Grid system topic, account-scoped delivery roles, narrowly scoped
runtime queue grants and four source subscriptions are deployed. Synthetic create/delete delivery
and non-expiring Event Grid messages are verified, with no-change Terraform convergence.
Configure `QueueBase64Encoded = true` for the observed single-object, Base64 UTF-8 envelopes
(`CasCap__DocumentIngestionConfig__QueueBase64Encoded=true` for environment configuration).
Isolated dead-letter capture and non-expiring replay are also verified; live retry policy was not
shortened for that test. Approved source mappings, runtime access, poison-send TTL and application
admission remain outstanding.
Do not enable the receiver merely because its transport is now operational.

Verified source-container URLs and their storage-account topic are now present in private local
application configuration. Production tenant/collection bindings are intentionally unset. Local
read-only PDF tests do not authorize activating ingestion or replace the remaining queue/runtime
handoff.

**Document Intelligence handoff:** Standard S0 is provisioned for low-volume, page-billed managed
extraction. The verified endpoint and resource ID remain in private infrastructure outputs and the
operator handoff, not this public ADR. Reuse the existing application service principal and its
credential bootstrap; no new managed identity, API key, OpenAI deployment or storage grant was
created. The public HTTPS endpoint requires Entra authentication; it is not a private endpoint.
For the proposed adapter, read the source through existing Blob permissions and upload PDF bytes,
then invoke `prebuilt-layout` with API version `2024-11-30` and
`outputContentFormat=markdown`, polling the asynchronous result. Do not pass a private credential-free
Blob URL and assume Document Intelligence can retrieve it. See the
[Layout model contract](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/layout?view=doc-intel-4.0.0).
The infrastructure work verified control-plane provisioning/RBAC and anonymous rejection.
The subsequent [application benchmark](../src/CasCap.App.Server.Tests/Tests/Integration/LiveDocumentIntelligenceQualityTests.cs)
verified authenticated analysis on two hash-identical cached PDFs with physical page selections
`2,3,6` and `6,7,12`, producing 14,289 and 5,988 Markdown characters in approximately 13.2 and
7.8 seconds including polling/export. It used no paid add-ons and saved private originals, full
Markdown/raw analysis, page comparisons and immutable request/operation receipts.
Source/page manifests prevent automatic resubmission after an ambiguous POST; existing receipts
resume polling. This is test-only access/contract evidence, not full Markdown fidelity, invoice
measurement, a production adapter or chunk/embedding acceptance. Keep submission behind the
approved sample/rights/data-handling boundary, verify physical-page content anchors before fixing
the budget, bound pages/retries and select chargeable add-ons explicitly. Existing preparation-quality
and ingestion activation gates remain unchanged.

Benchmark source files and README sections linked in this handoff are local, unpublished
application-workstream changes; the infrastructure handover does not publish that implementation.

#### OpenAI Access Handoff (2026-10-27)

The application workstream is paused for the separate infrastructure/access workstream.
No resource-provider registration, RBAC, deployment, subscription, storage or queue settings were
changed by this trial. Existing production ingestion remains disabled.

| Evidence | Observed result |
| --- | --- |
| Selected existing model | `gpt-5.5`, revision `2026-04-24`; deployment provisioning state `Succeeded` |
| First generation attempt | One `POST /openai/v1/responses`, physical page 6, PDF-only; HTTP 400 `SubscriptionNotRegistered`, with no model result or usage counters |
| Provider error category | `CheckAccess` rejected the request, reporting that the subscription was not registered with `Microsoft.CognitiveServices` |
| Independent control-plane check | `Microsoft.CognitiveServices` registration on the selected deployment's subscription was already `Registered` |
| Read-only application check | `GET /openai/v1/models` also returned HTTP 400 `SubscriptionNotRegistered`; no generation was submitted by these checks |
| Independent operator diagnostic | Non-generative model listing returned the same error; the operator token tenant matched the account subscription. This was an access diagnostic, not a benchmark identity fallback |
| Authentication | Existing application certificate/Key Vault bootstrap and shared Entra bearer handler; no API-key, CLI-identity or model fallback |
| Current audience | `https://ai.azure.com/.default`, matching the current [Responses v1 examples](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses); the first POST used the inherited Cognitive Services audience, and the corrected-audience read-only checks still failed |
| Recovery state | The copied-page provenance, immutable request manifest, HTTP status and raw rejected response remain in private session artifacts; no automatic generation retry occurred |
| Alternative-subscription discovery | Read-only control-plane checks advertise `gpt-5.5` revision `2026-04-24` with `GlobalStandard`, available quota and provider registration `Registered`; these are not provisioning or data-plane access evidence |

The five remaining planned generation requests were not attempted. No completed OpenAI response,
candidate Markdown or usage measurement exists; no billing conclusion is inferred from this.
Real subscription/account/deployment/identity coordinates stay in private configuration,
infrastructure outputs and operator evidence, never this ADR.

**Subscription fallback decision:** The owner requested Azure OpenAI in the alternative
subscription and confirmed that the working Document Intelligence account must remain unchanged.
The owner subsequently selected mirrored Speech, Azure OpenAI, Document Intelligence and Free
Azure AI Search across both subscriptions, using shared service/model configuration and distinct
accounts so applications can explicitly select where new requests are billed. Keep standard
consumption-based services; do not introduce provisioned model throughput, paid Search capacity
or other minimum commitments. Monitoring has separate pricing and is not implicitly duplicated.
Infrastructure parity does not copy Search indexes/data, implement automatic failover or establish
data-plane access. This is the requested configuration direction, not evidence of deployment.
Further IaC belongs to the separate infrastructure session, using this ADR as the handoff.
Prepare a parallel OpenAI account/deployment rather than moving or deleting existing resources.
Preserve existing service resources, Blob Storage, Event Grid, queues, runtime identities and
production ingestion settings; add the missing counterparts without replacing existing accounts.
Preserve existing Terraform backend/state ownership and provider
bindings. No IaC edits, state operations or infrastructure mutations were performed by the
application workstream for this fallback.

The infrastructure/access owner should:

1. Confirm the alternative target through private configuration and recheck model revision,
   regional/SKU availability and quota before planning. Discovery is not a capacity reservation
   or proof that the subscription's data plane works. Keep the approved `gpt-5.5` revision; return
   for a decision rather than silently substituting another model or processing geography.
2. Prepare the smallest additive IaC changes for the selected mirrored service set, including the
   parallel OpenAI account and deployment, reusing existing shared definitions without migrating other
   services. Retain the current application identity and grant only account-scoped inference
   access. Keep key authentication disabled. Review the exact saved plan and obtain separate
   apply approval; this handoff does not authorize an apply, replacement, deletion, new identity,
   API key, broader storage grant or production activation.
3. Preserve the blocked account and its evidence. The registration disagreement remains unresolved;
   moving the trial is a workaround, not a diagnosed root cause. Do not blindly re-register an
   already-registered provider or broaden roles. Escalate conflicting backend state to Azure
   support separately if needed; do not make that investigation a prerequisite for the parallel trial.
4. Verify the provisioned account/deployment and a converged follow-up Terraform plan. Prove
   non-generative access with the application's identity and the documented v1 audience.
   The existing `OpenAIDocumentAccess` test should return HTTP 200 without source submission.
   Obtain test approval before execution. Model listing alone does not establish Responses
   inference permission.
5. Return sanitized provisioning, model/revision, access and convergence evidence to this ADR;
   supply the endpoint and deployment configuration privately for the application cutover.
   Do not mark the blocker resolved from provider registration or provisioning alone.
   Reconcile the saved HTTP-400
   request explicitly and obtain approval before replacing that generation attempt. Preserve the
   rejected attempt and the same fixed run directory; do not delete request state or create a fresh
   directory to bypass replay protection. Reconfirm the page/model/token budget before continuing
   the six-candidate comparison.

**Infrastructure preparation evidence:** Shared configuration and a private saved plan are prepared,
not applied. The alternative-subscription plan adds Speech, regional OpenAI accounts, standard
deployments, Free Search and account-scoped runtime grants. It updates or destroys no existing
resources and leaves the working
Document Intelligence account unchanged. All new OpenAI deployments use `GlobalStandard`, local
keys stay disabled, and no new metric alerts are planned. Mocked validation covers the shared
module and alternative-subscription composition: 15 cases passed with the deployment's pinned
provider, alongside Terraform formatting/validation and scoped repository lint checks.
The original-subscription configuration passes
backend-free validation, but its live plan is blocked: subscription metadata reports `Enabled`
while the existing backend key lookup returns `ReadOnlyDisabledSubscription`. Preserve the backend,
account and original access-failure evidence; no original-subscription apply or authenticated
recovery is established.

**Credit-renewal hypothesis:** Exhausting the monthly credit of a credit-backed subscription can
disable its services when the spending limit is enabled. Microsoft documents automatic
reactivation at the start of the next billing period in its
[spending-limit guidance](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit).
This is a plausible explanation, not a verified billing diagnosis or a proven cause of the OpenAI
registration disagreement. The credit balance and renewal date have not been verified; do not
assume renewal is 1 November. A budget alert alone does not disable a subscription. Keep the
spending limit enabled unless removing it and accepting overage charges is separately approved.
After renewal, verify actual backend and application data-plane access rather than relying on
subscription metadata. Explicit endpoint selection can direct new requests to the alternative
subscription while the credit-backed one is unavailable; no automatic cutover has been implemented.

**Next infrastructure boundary:** Committing the configuration is not apply approval. Before an
apply, regenerate and review the private saved plan from the committed source, refreshing
subscription/quota checks and deployment-provenance tags. Apply only that exact separately approved
artifact, verify live resources and convergence, and obtain approval for the application's
non-generative access test. Do not retry the rejected generation, enable ingestion, remove the
spending limit or push changes merely because configuration has been committed.

See the [benchmark configuration and validation](../src/CasCap.App.Server.Tests/README.md#openai-pdf-only-and-hybrid-benchmark).
This handoff is an access blocker, not evidence that hybrid extraction improves quality.

#### Event Delivery And Storage Handoff

**Payload boundary:** Event Grid and the queues carry notification metadata and a blob reference,
not PDF bytes. Base64 applies to the notification JSON only. The credential-free blob URL is not
a SAS token or an access grant; the later processor must fetch content separately using its
authorized Azure identity and enforce the source-version contract. A deletion notification
identifies removed content; it cannot restore the deleted PDF.

The four synthetic notifications left queued by the Phase 2 test reference test blobs that were
subsequently deleted. They are transport evidence, not staged PDF work. Before enabling admission,
explicitly disposition those test artifacts using their recorded synthetic identities; never clear
the queue indiscriminately. Phase 3 replay used only an isolated queue, which was removed afterward.

The account's public HTTPS endpoint is reachable, not anonymously readable and not a private
endpoint. The existing application service principal has inherited Blob read/write/delete access;
it is not a managed identity. This verifies the role assignment, not authenticated access from the
intended worker pods. Separate ingestion-queue message read/write/delete and poison-send grants
now exist, but access from the worker has not been verified. Keep actual identity/bootstrap values
in private operator configuration.

**Agreed Phase 0 policy; producer configuration still pending:** Blob soft delete is verified
false, container soft delete and Blob versioning are unconfigured, and no storage lifecycle
policy exists. Keep source PDFs and delivery dead-letter blobs until explicit operator deletion,
with no automatic lifecycle expiry. Deleted/overwritten originals cannot be recovered through
these disabled features; keep an independently authorized original where recovery is required.
Use non-expiring ingestion and poison messages with operator-controlled cleanup/replay, and a
configurable Event Grid retry budget of 24 hours / 30 attempts before delivery dead-lettering.
Event Grid retry lifetime and successfully queued message lifetime are separate controls.

Message TTL must be explicit at each producer, including Event Grid delivery, poison sending and
operator replay; it is not a queue-wide resource setting. The current
[poison-send adapter](../src/CasCap.App.Server/Services/AzureDocumentIngestionQueue.cs) omits TTL,
so its default seven-day expiry does not yet meet the agreed policy. Implement and verify that
application-side setting before declaring retention ready. See
[Event Grid queue delivery lifetime](https://learn.microsoft.com/en-us/azure/event-grid/delivery-properties#configure-time-to-live-on-outgoing-events-to-azure-storage-queues)
and [Storage Queue message TTL](https://learn.microsoft.com/en-us/rest/api/storageservices/put-message#uri-parameters).

The locally tested storage-module enhancement accepts variable-driven container, queue and table
name collections with empty defaults, private containers and name-keyed resource-ID outputs.
Container and queue creation has been verified live using local module files; tables are empty,
so their live lifecycle is not yet proven. Module publication and the caller's immutable release
pin remain pending. Identity grants stay caller-owned. Adding a source container alone is not
ingestion onboarding: its event binding and approved tenant/collection mapping must also exist.
Resource names are selected privately; trusted mappings and the installation-qualified leadership
identity remain operator-supplied inputs, not inferred application metadata.

**Verified manual replay contract:** Repair the destination first and check whether the event
was already admitted. Replay each original event separately, preserving its event ID, storage
topic, subject, source version and core payload fields; remove dead-letter diagnostics rather
than enqueueing the entire dead-letter batch. Encode once as Base64 UTF-8 and explicitly use
TTL `-1`. Verify queue delivery or a durable admission receipt before operator-approved archival
or removal of the dead-letter original. This is a proven operator procedure, not an implemented
automatic application replay feature or proof of application deduplication.

Supply these values privately to
[`DocumentIngestionConfig`](../src/CasCap.App.Server/Models/_DocumentIngestionConfig.cs), under
`CasCap:DocumentIngestionConfig` (environment variables use `__` between path segments):

| Field | Handoff contract |
| --- | --- |
| `QueueUri` | Existing queue's credential-free HTTPS URI; one queue path segment, no SAS query |
| `PoisonQueueUri` | Different existing private queue; worker sends invalid/unassigned envelopes here before deleting source delivery |
| `QueueBase64Encoded` | Set `true` for the live transport verified on 2026-10-26: Base64 UTF-8, one EventGridSchema object per message. The code default `false` means raw JSON and does not match this deployment |
| `Sources:0:Topic` (and subsequent entries) | Exact storage resource topic from a delivered Event Grid-schema event, not a display name |
| `Sources:0:ContainerUri` | Canonical credential-free HTTPS container URI, not an individual blob/directory; each container maps exactly once |
| `Sources:0:TenantId`, `Sources:0:CollectionId` | Operator-approved application tenant and tenant-local collection; never derived from caller/event metadata |
| `LeadershipResource` | Operator-chosen stable installation-qualified Redis election identity, shared by all ingestion replicas |
| `PollIntervalMs`, `VisibilitySeconds` | Defaults 2000 ms / 60 s; supported ranges 100-60000 ms / 30-600 s. Confirm visibility exceeds measured short-admission latency |
| `ProcessingEnabled`, `ProcessingTimeoutSeconds` | Application-owned, not IaC readiness signals: default `false` / 300 s (30-900 s). Keep disabled until processing/preparation migrations, isolated provider checks and the identity/delivery handoff are complete |

The infrastructure owner should update the Phase 0 checkboxes with sanitized observed outcomes:

- Single-object EventGridSchema delivery, `BlobCreated`/`BlobDeleted`, `PutBlob`/`DeleteBlob` and
  exact source-container topics/subjects are verified with synthetic blobs. Other configured
  final-write APIs remain untested. Isolated dead-letter capture/replay is verified with a shortened
  test-only retry budget; output/dead-letter containers are excluded by the live source prefixes.
- Both queues now exist. Configure Event Grid destination authorization and the Agentizr identity
  separately: least-privilege queue receive/update/delete on the source queue, send on poison, and
  source blob read for the later processor. Application upload/delete permissions are a separate
  forthcoming UI/API requirement. Runtime does not create containers, queues or subscriptions.
  Verify message-update permission for the adapter's lease release/renewal methods: the built-in
  `Storage Queue Data Message Processor` role alone does not include message-write permission.
- Supply the existing Azure identity/bootstrap configuration privately; verify DNS, firewall,
  workload-identity/RBAC propagation and access from intended pods without broad account keys.
- Distinguish the application poison **queue** from Event Grid delivery dead-letter **Blob storage**.
  Event Grid non-expiring delivery and isolated operator replay are verified. Application poison
  TTL, runtime permissions and operational replay ownership still require handoff. An encoding
  mismatch fails the receiver; configure the verified Base64 setting before enabling it.
- Stage authorized PDFs and capture safe evidence that create/delete notifications reach the queue.
  Do not copy PDFs, event bodies, real resource IDs or deployment endpoints into this public ADR.
- Confirm the optional Search service's capability/identity/network readiness independently;
  provisioning it does not select a backend or prove vectorization. pgvector remains first.

The application workstream can continue local admission development while this handoff is
incomplete. Before a live admission run, apply the additive application migration externally,
supply shared Redis with distributed locking and the agreed leadership resource, use a compatible
Debug build until the shared Release APIs are published, and explicitly select `ingestion` or
`agentizr,ingestion`. The default API role does not consume notifications. Pgvector activation is
not required for admission-only persistence, but remains a prerequisite for vector processing and
retrieval. Read-only follow-up has now verified the extension rollout and a vector operation;
this does not prove full RAG processing.

### Phase 1: Accept The Decision And Prove Compatibility

- [ ] Accept or amend this ADR; keep acceptance separate from implementation progress.
- [x] Select stable managed libraries and bounded offline preparation defaults: `PdfPig` 0.1.16,
  `Microsoft.ML.Tokenizers` 2.0.1 and its `Data.Cl100kBase` 2.0.1 vocabulary package. These are
  Debug-only dependencies; tokenization runs offline without downloading model data.
  [PDF preparation](../src/CasCap.App.Server/Services/PdfDocumentPreparationService.cs) uses
  PdfPig's content-order extraction, not raw `page.Text`.
- [ ] Select the multilingual embedding profile, live upload/download policy, rights/retention,
  quotas and target hardware before processing real document contents (R3, R7).
- Alternatives considered:
  [Microsoft.Extensions.DataIngestion](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/data-ingestion)
  offers tokenizer-backed header/section/semantic-aware chunkers. Its NuGet installation guidance
  currently uses `--prerelease`, so evaluate/pin compatibility and approve prerelease use before
  adopting it; the PoC instead uses a smaller page-local splitter with `Microsoft.ML.Tokenizers`.
  The [MarkItDown reader](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion.MarkItDown/)
  wraps an external executable or MCP service, not a managed-only PDF parser. Prefer reusing
  extraction/tokenization libraries without adding that deployment dependency for the PDF-only PoC.
- [x] Verify the live extension prerequisite: the database controller reports successful activation,
  the extension is installed in the expected schema, and a read-only vector operation succeeds.
- [ ] Verify pgvector/provider/package compatibility and framework search/history APIs using the
  repository's pinned dependencies (R6).
- [ ] Build an authorized evaluation set of at least 40 questions, at least 20 per initial use case,
  including German inflections/umlauts, device identifiers, edition/CEFR filtering and unanswerable
  questions. Use synthetic or appropriately licensed CI documents (R4, R8).

### Phase 2: Deliver Central Upload, Storage And Search

- [x] Implement the local gated durable lifecycle: serial claim, three attempts, persisted 30-second
  retry, deadline recovery and revision/claim-checked completion. Focused credential-free run:
  78 cases passed, zero failed/skipped (69 role/ingestion plus nine existing endpoint smoke cases).
  SQLite/manual-clock and fake-runner tests do not establish PostgreSQL races or live failover.
  The full Debug solution builds without warnings/errors; the current EF model has no pending
  changes. The scaffolded additive processing migration and offline SQL were reviewed but not
  applied. The lifecycle is committed locally, not deployed.
- [x] Implement offline PDF preparation using synthetic documents: 25 MiB source cap, 200 pages,
  two million extracted UTF-16 characters and 5,000 chunks; defaults are 512 actual tokens with
  at most 64-token overlap. Startup limits and per-call chunk settings are validated.
  [Chunking](../src/CasCap.App.Server/Services/DocumentChunkingService.cs) preserves page numbers,
  exact extracted-text offsets and Unicode scalar boundaries; the captured profile includes
  algorithm, encoding, library versions and token budgets. Oversized, malformed, encrypted and
  wholly no-text inputs have explicit outcomes, with no partial chunks on rejection; mixed
  documents report pages without text. All 42 focused offline cases passed, including exact
  resource thresholds, Unicode coverage, cancellation and nonseekable streams.
  This is local preparation only: no source download, persisted chunks, embedding or activation.
  The combined preparation/ingestion/role/endpoint regression run passed all 120 cases with zero
  failures/skips; the full Debug solution built with zero warnings/errors.
- [ ] Wire the real PDF processor and prove conditional downloads, idempotent staged output and
  fenced generation activation against isolated PostgreSQL before enabling processing.
- [x] Add a Debug-only [Blob download adapter](../src/CasCap.App.Server/Services/DocumentBlobDownloadService.cs)
  using `CasCap.Api.Azure.Storage`, not an application-owned replacement. The shared library now
  accepts an existing container client without provisioning and offers ETag-conditional streaming
  reads. Agentizr rechecks the claim's tenant/collection/container binding and canonical location,
  decodes the name once, rejects relative path segments and uses its existing runtime identity.
  Azure 404/412 and transient errors
  propagate, including failures during later range reads; the future processor must classify these
  and discard partial output. Its 64 KiB SDK buffer is not a total download limit; consume through
  bounded preparation. The adapter/DI tests passed all 24 offline cases, and the shared library's
  12 SDK HTTP-pipeline cases passed on .NET 10, including a mid-read revision change and read
  cancellation. No live download, worker
  hookup, persisted output, migration or deployment is claimed. Shared APIs remain unpublished.
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
gate. This ADR is saved locally. The SmartHaus ownership-handoff closure is verified. The local
admission foundation is deployed with ingestion disabled; its external migration and basic API
rollout checks succeeded. Gated job processing is implemented and locally validated, not deployed.
The infrastructure
workstream supplied the sanitized account/Search observations recorded in Phase 0; private
resource coordinates and deployment artifacts remain outside this public record. Ingestion
transport, worker access and full document processing/retrieval remain
unverified. A read-only follow-up verified pgvector activation and a vector operation; it did not
mutate the database or establish application compatibility.
