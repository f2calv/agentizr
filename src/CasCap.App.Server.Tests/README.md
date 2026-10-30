# CasCap.App.Server.Tests

Unit, in-memory protocol, PostgreSQL/Redis integration and opt-in live-runtime tests for
`CasCap.App.Server`. Most cases are credential-free; PostgreSQL and Redis cases require the local Compose services and the
live cases require explicitly configured deployment credentials.

## Purpose

Protocol tests launch the real application through `WebApplicationFactory<Program>` with scripted
execution or evaluation substitutes. Unit tests cover orchestration, authentication, metrics and
state; PostgreSQL integration cases exercise isolated per-fixture databases on reusable Compose services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 5 | 9 | Verifies status and health endpoints, the frontend shell without capturing API routes, disabled MCP, all 19 named runtime controller actions and inferred API response metadata |
| `AdministrationApiTests` | 7 | 8 | Verifies open-Development app, agent and provider discovery including the built-in Azure OpenAI preparation definition, guarded activation conflicts, enum names, editor drafts that keep unedited fields, required antiforgery tokens and redirect-free rejection of unknown apps |
| `AdministrationDefinitionUiTests` | 5 | 9 | Static component rendering against synthetic HTTP responses verifies template discovery without mutations, selected-version actions, active pointers outside history, suggested versions and opt-in activation; typed-client checks cover hidden version collisions and lookup errors |
| `DefinitionVersionSuggestionTests` | 2 | 7 | Verifies unused UTC date-and-revision suggestions, occupied revisions, gaps, custom identifiers and culture independence |
| `AdministrationSignInTests` | 6 | 13 | With sign-in enabled, verifies 401/403 per-app assignment, assigned-app listing, session description, administrator audit identity, logout antiforgery, local-only return URLs and the setup page for ignored forwarded HTTPS |
| `AdministrationReplicaTests` | 1 | 1 | Against real PostgreSQL: an antiforgery token issued by one host replica is accepted by another through the shared Data Protection key ring |
| `AgentizrMcpEndpointTests` | 3 | 3 | Verifies MCP discovery of only the read-only status tool, its structured result, and `Origin` rejection |
| `AgentRuntimeProtocolTests` | 9 | 12 | Verifies typed-client execution, streaming, DTO/JSON validation problems, session controls and instruction disclosure |
| `AgentDefinitionAdministrationProtocolTests` | 13 | 23 | Verifies publication Location, evaluation/activation, rollback, deactivation, audit attribution, policy/credential rejection, conflicts and bounded query binding |
| `TenantSecurityStartupTests` | 8 | 12 | Verifies anonymous rejection, policy separation on all runtime and administration controller actions, the setup page shown instead of an unusable frontend, required JWT/PostgreSQL and optional Redis in Production |
| `AgentExecutionCoordinatorTests` | 7 | 12 | Verifies execution/session orchestration, stateless isolation and effective read policy, all six rejected session mutations and the actual HTTP 409 problem response |
| `DocumentPreparationAgentTemplateTests` | 3 | 3 | Verifies the authoritative built-in instructions, bounded secret-free template, editor preservation and actual runtime execution without discovery tools, time context, overrides, persisted sessions or content history |
| `AgentRuntimeBuiltInToolsTests` | 3 | 5 | Verifies root runtime tools, reachable delegation metadata, tenant-local time formatting and opt-out |
| `AgentRuntimeMetricsTests` | 5 | 5 | Verifies execution and bounded HA dimensions, ownership balance, duration and conflict outcomes |
| `CommonAgentExecutorTests` | 7 | 7 | Verifies tool composition/discovery, unreachable-source fallback and per-round-trip history projection without binary content |
| `AgentDefinitionEvaluatorTests` | 4 | 6 | Verifies fixture-backed delegation grading, failed tool selection and answers, pass-rate thresholds, and request validation |
| `TenantStateIsolationTests` | 2 | 2 | Verifies process-local tenant isolation and collision-free keys |
| `AgentSessionLockTests` | 3 | 5 | Verifies fail-fast Development ownership, resource isolation, cancellation and idempotent release |
| `AgentSessionConflictTests` | 1 | 8 | Verifies all session mutation routes return HTTP 409 on overlap, including streaming before headers |
| `PostgresAgentStateTests` | 8 | 8 | Durable state/cache recovery, isolation, revisions, connection-loss fencing, two-host contention and real-operation HA metric privacy/balance |
| `AgentRunHistoryTests` | 4 | 7 | Durable messages/tools, redaction/binary exclusion, success/failure/cancellation, interruption recovery, opt-in and archive preservation after reset |
| `AdministrationAccessTests` | 5 | 14 | Verifies configuration-only administrator assignment, that an assignment alone never creates an app, open-Development conditions and route-selected administration tenancy |
| `AdministrationSetupServiceTests` | 3 | 5 | Verifies setup pages report setting status without values, encode request input and detect ignored forwarded HTTPS |
| `AuthenticatedTenantContextTests` | 4 | 4 | Verifies caller-derived tenant identity, missing/unmapped caller rejection, and opaque Redis keys |
| `AgentRuntimeAuthorizationTests` | 4 | 6 | Verifies delegated scopes, application roles, and caller-to-tenant mapping validation |
| `PostgresAgentDefinitionStoreTests` | 5 | 5 | Verifies immutable publication, active-version changes, duplicate rejection, history, tenant isolation, paged agent discovery and stale active-version conflicts |
| `PostgresAgentDefinitionStoreIntegrationTests` | 9 | 9 | Against real PostgreSQL: migrations, `jsonb` storage and round trip, sequential and concurrent duplicate-version conflicts, advisory-lock-serialised concurrent activation, the tenant-scoped activation lock, audited deactivation, tenant isolation, discovery queries and stale active-version conflicts |
| `CachedAgentDefinitionStoreTests` | 3 | 3 | Verifies immutable payload caching, pointer invalidation across activation/deactivation and absolute expiry before authoritative reload |
| `ConfigurationMcpCredentialStoreTests` | 2 | 2 | Verifies tenant-scoped Authorization resolution and shared Basic credential fallback |
| `ConfigurationProviderCredentialStoreTests` | 2 | 5 | Verifies the runtime's Entra credential is offered only to a tenant's opted-in providers |
| `SessionCompactionConfigTests` | 3 | 6 | Verifies per-provider compaction resolution (tenant override, runtime entry, defaults) and startup validation |
| `LiveAgentRuntimeSmokeTests` | 1 | 1 | Opt-in authenticated run, streaming, session inspection and cleanup against a complete live runtime |
| `RuntimeLoggingTests` (Debug only) | 1 | 2 | Real PostgreSQL verifies application database-name override, actual-switch status, forwarding, category overrides, cache retention and expiry to Information or Debug startup baselines |
| `RuntimeLoggingBootstrapTests` (Debug only) | 4 | 6 | Offline bootstrap selection, missing/ambiguous connection rejection, normalized application target and unchanged disabled HTTP status shape |
| **Runtime baseline subtotal** | **147** | **225** | Excludes Debug-only logging; role/ingestion additions are listed separately below |

The runtime logging soak is Debug-only. Run its focused cases with:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug `
  --filter-class CasCap.Tests.Integration.RuntimeLoggingTests `
  --filter-class CasCap.Tests.Integration.RuntimeLoggingBootstrapTests
```

Observed targeted Debug results: all eight logging/bootstrap cases passed, including two against
real PostgreSQL. Previously, nine existing endpoint smoke cases passed with runtime configuration
disabled. This does not claim
a complete Debug or Release suite run. The default Release CI path does not execute this provisional
Debug-only application case; the reusable providers' acceptance suite is wired into their own CI.

Counts expand every `[InlineData]` row; this project uses no `MemberData` or `ClassData` providers.

Reusable secret-free agent, provider, runtime-definition, and publication builders live in
`AgentDefinitionTestData`. Protocol and built-in tests consume these definition-contract fixtures so
test definitions do not drift into application-specific `AIConfig` setup.
`ScriptedEvaluationAgentFactory` replaces the model provider with a scripted chat client for
evaluation tests.

## Document Ingestion PoC

These additional role/ingestion tests are separate from the runtime baseline inventory above.

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| `FeatureConfigTests` | 3 | 11 | Default, normalization, duplicate, unknown, empty and incompatible finite-role selections; Debug and Release |
| `DocumentIngestionConfigTests` | 7 | 23 | Synthetic source/queue configuration, canonical HTTPS endpoints, visibility, processing/sync deadlines and recurring five-field calendars |
| `DocumentIngestionAdmissionTests` | 9 | 20 | Commit-before-ack, infrastructure/argument failures, cancellation, malformed envelopes, source isolation and sequencer normalization |
| `DocumentIngestionInboxTests` | 16 | 19 | SQLite admission, disabled/missing processors, terminal outcomes, exact retry delay/attempt limit, expired and replaced claims, supersession, cancellation, deadline cleanup and unknown faults |
| `DocumentIngestionLeadershipTests` | 3 | 4 | Fake-runner ownership, cancellation on loss, gated processing resolution and clock-driven startup/hourly durable synchronization |
| `DocumentIngestionCompositionTests` | 1 | 4 | Real local/hybrid-router DI, idempotent API/ingestion source binding, disabled/enabled gates, explicitly removed processor rejection and test substitution; no transport starts |
| **Role/ingestion subtotal** | **39** | **81** | All but the role-parser class are Debug-only |

Run the focused credential-free selection from the repository root:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug `
  --filter-class '*FeatureConfigTests' '*DocumentIngestionConfigTests' `
  '*DocumentIngestionAdmissionTests' '*DocumentIngestionInboxTests' `
  '*DocumentIngestionLeadershipTests' '*DocumentIngestionCompositionTests'
```

The database/DI fixtures are under `Integration/` and use synthetic, test-owned setup rather than
any live-service configuration; no Azure, PostgreSQL or Redis connection is made.
The composition fixture exercises `runtimeEnabled=true`. Full ingestion-only startup calls the
existing eager Redis registration and therefore needs a separately approved isolated Redis check;
the worker's tenant-local runtime adapter is independently covered without HTTP tenancy.
`Roles` covers the 11 parser cases; `Ingestion` covers the remaining 70, including 27 also tagged
`Integration`. The processing tests use a manual clock/timer, not retry-duration sleeps.
Neither a fake ownership callback nor SQLite validates actual Redlock renewal,
PostgreSQL advisory-lock concurrency, Azure queue encoding/leases/poison delivery, migration process
exit or worker-only/combined host routes.

Earlier baseline combined Debug run: **178 passed, zero failed/skipped**: the previous 70 role/ingestion cases, nine
existing endpoint cases, 42 preparation cases, 21 download cases and 36 staged-persistence cases.
This is not a
full-suite or live-provider result. See
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#poc-limits-and-deliberately-deferred-polishing)
for the remaining acceptance gates. The real processor is exercised with synthetic streams and
SQLite; no live-provider race test or blob-to-answer flow is claimed.

### Document Source Synchronization

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`DocumentSynchronizationTests`](Tests/Integration/DocumentSynchronizationTests.cs) | 11 | 23 | Matching SQLite/native PostgreSQL contracts, failed inventories, content retirement, late-event coalescing, deadline takeover and additive migration upgrade |

These Debug-only cases use a fake read-only Blob inventory and synthetic documents/providers; no
Azure or paid call occurs. PostgreSQL cases create, migrate and drop only uniquely named test-owned
databases using the existing fixture. The upgrade case preserves an admitted row from
`20261028120307_DocumentMarkdownReviewGenerations`. A completed scan is not permission to delete
content when HEAD fails; native tests also prove expired run IDs and retired processing claims
cannot publish stale content. Quoted and unquoted ETags are treated as the same revision.

The ownership case in the preceding table uses `FakeTimeProvider` and explicit timer/transaction
gates, not sleeps. It proves startup work begins only under ownership, the next UTC hour commits a
durable request even while admission is blocked, and ownership cancellation joins both loops.
It does not prove real Redis renewal or live Azure permissions.

Run the synchronization contract with approved local PostgreSQL available:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class '*DocumentSynchronizationTests'
```

Observed validation: **164 passed, zero failed/skipped**, combining the 81 role/ingestion cases,
23 synchronization cases and affected SQLite/PostgreSQL preparation persistence regressions.
The full Debug solution built with zero warnings/errors; EF reported no pending model changes and
the reviewed migration SQL was additive. The separate Redis-host handover case could not connect
to its required Redis service and is not included in that passing selection.

### Offline PDF Preparation

These additional Debug-only cases use generated synthetic PDFs and the packaged tokenizer
vocabulary; they need no network, credentials, live documents or external services.

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| `DocumentChunkingTests` | 5 | 15 | Actual token limits and overlap, exact German/emoji/CJK coverage, scalar boundaries, advancing windows, per-call profile identity/JSON and cancellation |
| `DocumentPreparationConfigTests` | 3 | 6 | Default resource limits, invalid caps and nested chunking validation |
| `PdfDocumentPreparationTests` | 15 | 21 | Generated PDF extraction/page attribution, independent per-call settings, exact byte/page/text/chunk thresholds, malformed/encrypted/no-text rejection, borrowed/current-position/nonseekable streams, bounded reads, cancellation and propagated I/O failures |
| **Preparation subtotal** | **23** | **42** | Local preparation only, not an ingestion processor or persisted search generation |

Run just this group from the repository root:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=PdfPreparation'
```

Observed targeted Debug run: **42 passed, zero failed/skipped**. These cases do not establish
hostile-PDF process isolation, OCR, layout quality on real documents, embedding-model compatibility
or live Blob-to-answer behavior.

All preparation cases are also included in the current 178-case regression run. The full Debug
solution built with zero warnings/errors; neither check ran the complete application test suite.

### Offline Markdown-Headings Projection

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`DocumentMarkdownChunkingTests`](Tests/Unit/DocumentMarkdownChunkingTests.cs) | 17 | 30 | Debug-only synthetic heading projection, tagged `MarkdownChunking` |
| [`DocumentMarkdownReviewTests`](Tests/Unit/DocumentMarkdownReviewTests.cs) | 10 | 32 | Explicit source-role exclusions, unchanged provenance, atomic blocks, audit/identity, stale/invalid annotation rejection; tagged `MarkdownChunking` |
| [`DocumentMarkdownStructureTests`](Tests/Unit/DocumentMarkdownStructureTests.cs) | 10 | 33 | Reviewed heading levels, paragraph promotion, layout/relational distinction, column-major entries, empty/additional cells, exact Unicode/inline spans, atomic budgets and invalid decisions; tagged `MarkdownChunking` |
| **Projection subtotal** | **37** | **95** | Synthetic, Debug-only; no provider or database access |

Synthetic Markdown covers ATX/Setext ancestry,
sibling transitions, contiguous/missing physical pages, code fences and nested pseudo-headings,
source heading/body spans, complete-input token ceilings, scalar-safe Unicode overlap, soft targets,
semantic-unit preservation, explicit oversized structured-unit rejection, profile/digest identity,
resource limits and cancellation. The existing 15 page-local cases remain unchanged.

The earlier combined chunking, hybrid, template and definition-UI selection passed **133 cases**, zero
failed/skipped. The full Debug solution and Release server build passed with zero warnings/errors;
Release excludes this projection and its Markdig dependency. No Azure call, paid acceptance,
Docker service, generation persistence or searchable activation was performed.

Parser regressions intentionally use raw heading source spans: normal Markdig inline processing
clears heading and paragraph `Lines`. Single-line promotion therefore examines the original span,
not the cleared line collection. Pipe-table `ColumnIndex` is unset; parsed child order defines
columns. The net10.0 parser may pad rows/headers with spanless empty cells; those cannot invent
content or constrain the actual column count. Token tests also distinguish a soft target from the hard ceiling:
a Unicode scalar that exceeds the soft budget must still fit when the remaining hard budget permits.

#### Retained Real-Document Review

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`DocumentPreparationOfflineReviewTests`](Tests/Unit/DocumentPreparationOfflineReviewTests.cs) | 8 | 14 | Thirteen synthetic export/coverage cases plus one separately gated local-artifact check; tagged `OfflineDocumentReview` |

The local-artifact case reads only the existing export's encoded transcription elements, never its
managed drafts or annotation lists. It verifies PDF SHA-256 pins, original page selections and
validated-operation metadata against the retained usage exports. It reuses the production chunker
without extracting source text, opening the paid ledger, resolving credentials or calling providers.
The narrow export reader is not a general-purpose HTML importer.

After explicit approval, set `CASCAP_RUN_DOCUMENT_OFFLINE_REVIEW=true`,
`CASCAP_DOCUMENT_REVIEW_INPUT` to the retained review directory and
`CASCAP_DOCUMENT_REVIEW_OUTPUT` to a new absolute private directory, then run:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Unit.DocumentPreparationOfflineReviewTests CasCap.Tests.Unit.DocumentMarkdownChunkingTests CasCap.Tests.Unit.DocumentPreparationCandidateTests
```

The approved run passed **52 cases**, zero failed/skipped. With the unchanged 512-token maximum
and 64-token paragraph overlap, the six saved transcriptions produced:

| Source sample | Original pages | Chunks | Complete-input tokens/chunk | Fragmented chunks |
| --- | --- | ---: | --- | ---: |
| Device manual | 2, 3, 6 | 27 | 12-481 | 0 |
| Vocabulary | 6, 7, 12 | 10 | 11-504 | 0 |

Checks require deterministic output, exact heading/body projection, scalar-safe source ranges,
complete non-heading/non-whitespace coverage and no ancestry across missing physical pages.
The vocabulary has one peer heading with no intervening body; its span is explicitly reported as
unembedded and retained in the page export, not silently counted as covered or made a false ancestor.
A synthetic dropped-body regression proves that this allowance cannot hide lost body text.
This is mechanical acceptance of six retained transcriptions, not independent chunk-boundary,
warning/procedure, vocabulary/example, diagram or retrieval-quality approval.

The new private output includes the original/draft/agent panels, collapsed chunk previews,
`source-N.pages.json`, `source-N.chunks.json` and `validation.json`. It records the five original
artifact hashes and verifies they remain unchanged. No paid run was repeated, original artifact
overwritten, service started or generation persisted. A rejected structured unit remains an explicit
failed check, with rejection evidence retained rather than a larger-budget retry.

#### Reviewed Source Roles

For an explicitly approved annotation run, additionally set
`CASCAP_DOCUMENT_REVIEW_ANNOTATIONS` to an absolute private directory containing
`source-1.review.json` and `source-2.review.json`, then use a fresh output directory.
Each file follows the [versioned source-review contract](../CasCap.App.Server/Models/DocumentMarkdownSourceReview.cs)
(current schema 2, including explicit `exclusions`, `headings` and `layoutTables` collections),
not a text-matching cleanup rule. Without this optional directory, the original unannotated
projection remains available. No review identity or source path is inferred from document text.

The earlier schema-1/profile-2 reviewed-role run passed **130 cases**, zero failed/skipped: 32 review-contract,
30 heading, 15 page-local, ten export/offline, 12 candidate and 31 hybrid-worker cases.
The full Debug solution and restored Release server build passed with zero warnings/errors.
When switching conditional Debug project references to Release packages, restore the matching
configuration; stale Debug assets alone do not validate Release.

With the same six transcriptions and 512-token ceiling:

| Sample | Reviewed exclusion spans | Chunks before/after | Complete-input tokens/chunk after |
| --- | ---: | --- | --- |
| Device manual | 31 (including 18 navigation spans) | 27 / 23 | 21-302 |
| Vocabulary | 8 | 10 / 9 | 60-504 |

No chunk fragmented. The checker verifies no excluded source span contributes body or heading
text, complete retained-body coverage, unchanged source digests and deterministic reviewed
projection identities. The five original artifacts plus two annotation inputs remain unchanged.
The private preview displays exclusion roles/ranges/reasons separately from embeddings.
One vocabulary heading-only span remains explicitly unembedded.

Receipts and normalized page exports were not rewritten; that profile-version-2 projection retains
the whole-block review audit. Its archived schema-1 annotations remain historical evidence,
not inputs accepted by the current validator.

#### Verified Heading And Layout Corrections

The current schema-2/profile-3 projection adds explicit structural decisions verified against original
physical pages. The approved focused selection passed **109 cases**, zero failed/skipped:
33 structure, 32 source-review, 30 heading and 14 export/offline cases. To repeat that selection,
use the approved private input/output/annotation settings above and filter these four classes:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Unit.DocumentMarkdownStructureTests CasCap.Tests.Unit.DocumentMarkdownReviewTests CasCap.Tests.Unit.DocumentMarkdownChunkingTests CasCap.Tests.Unit.DocumentPreparationOfflineReviewTests
```

| Sample | Heading decisions | Layout-table decisions | Chunks | Complete-input tokens/chunk |
| --- | ---: | ---: | ---: | --- |
| Device manual | 7 | 0 | 23 | 31-302 |
| Vocabulary | 2 | 4 | 10 | 52-471 |

The six corrected manual prefixes retain chapter/subsection ancestry; all 23 manual bodies and
body provenance match the prior reviewed-role projection. Two verified alphabet markers become
headings; no missing alphabet ancestor is fabricated across the page gap. Four vocabulary layout
tables yield 35 independent entries in column-major order, each group kept whole, while the
relational clock/example table remains unchanged. All chunks fit the unchanged 512-token ceiling
without fragmentation.

Coverage requires every cell's exact source span and full ordered list projection before allowing
only table formatting to be absent. Dropped-cell and extra-content regressions prevent the formatting
allowance from hiding lost text. The five original artifacts and two new annotation inputs remain
unchanged; one heading-only span remains explicitly unembedded. Review annotations, reasons and
usage never enter embedding text. Full Debug and configuration-restored Release builds pass with
zero warnings/errors. No service, provider, paid replay, generation persistence or activation runs.

Contents/navigation remain source metadata. Broader warning/procedure and entry/example
classification, visual associations, reviewer authorization in the future UI and approved generation
persistence remain separate gates. This bounded correction run is not whole-document semantic or
retrieval-quality approval.

### Conditional Blob Downloads

`DocumentBlobDownloadTests` adds **5 methods / 21 Debug-only cases**, tagged `BlobDownload`.
They verify delegation to `CasCap.Api.Azure.Storage`, source/tenant/collection revalidation,
canonical URLs, exactly-once name decoding, required ETags, cancellation and failure propagation.
The shared library is exercised with an SDK-client test double; the library's separate tests
verify actual SDK HTTP requests through an offline handler. Neither suite accesses live storage.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class '*DocumentBlobDownloadTests' '*DocumentIngestionCompositionTests'
```

The current download/composition selection has 25 cases: 21 download and four composition cases,
all passed within the 178-case run.
The full Debug solution builds with zero warnings/errors. No live identity,
conditional Blob access, durable output or generation activation is established by these checks.

### Prepared Document Persistence

`DocumentPreparationPersistenceTests` adds **17 methods / 36 Debug-only cases**, tagged
`PreparedDocuments` and `Integration`. A private in-memory SQLite connection, manual clock,
save interceptors and fake Blob streams exercise the real PDF processor and relational store.
Coverage includes original-byte/profile/page metadata, immutable retry deduplication, exact
source/claim fences, post-insert rollback on expiry/failure/cancellation, tenant constraints,
separate profiles, failure classification and partial source-read rejection.
Synthetic PDF generation is shared through `DocumentPreparationTestData`.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.DocumentPreparationPersistenceTests
```

All 36 cases passed within the 178-case combined run. The full Debug solution builds cleanly;
offline EF checks report no pending model changes and the additive preparation migration SQL was
reviewed. No deployment migration was applied; the provider acceptance below applies the full chain
only to disposable local databases. SQLite alone does not prove PostgreSQL locks/races, live identity,
JSONB storage, embeddings, searchable activation or cleanup/retention behavior.

### Review-Required Markdown Persistence

[`DocumentMarkdownGenerationTests`](Tests/Integration/DocumentMarkdownGenerationTests.cs) adds
**18 methods / 47 Debug-only cases**, tagged `PreparedDocuments` and `Integration`.
The real hybrid processor writes synthetic managed/agent receipts to a private SQLite database;
explicit storage then preserves normalized Markdown, separate notes, structural review, profile,
heading hierarchy and cell/body spans without another provider call.

Cases cover immutable retries and changed review/profile generations; tenant/collection/document/
job/sequence/ETag/Blob/plan fences; replacement/deletion; finished review-only state; malformed,
missing or mismatched receipts; exact aggregate Markdown-plus-note character limits; receipt/page/
chunk limits; cancellation before/after insertion; post-save failure/source rollback; pre-write
receipt changes; expired/released original leases; cross-tenant foreign keys; recursively reordered/
reformatted receipt/snapshot JSON; and stored version/identity corruption.
No page-local prepared chunks, approved/searchable state or active pointer is created.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.DocumentMarkdownGenerationTests CasCap.Tests.Integration.DocumentHybridPreparationTests CasCap.Tests.Integration.DocumentPreparationPersistenceTests
```

The focused selection passes **114 cases**, zero failed/skipped: 47 review-generation, 31 existing
hybrid-receipt and 36 page-local persistence cases. A source-fence `ToQueryString` check uses Npgsql
without opening a connection. Additive migration SQL and current model consistency are checked
offline. Reordered JSON is a semantic regression, not proof of actual PostgreSQL JSONB storage.
Full Debug solution and configuration-restored Release server builds pass with zero warnings/errors.
No PostgreSQL/Compose service, database migration execution, paid call, live write, deployment,
management authorization, semantic/visual approval, embedding or activation is covered by this run.
Provider-backed acceptance for this path is recorded below; management and quality approval are
still separate requirements.

### PostgreSQL Review-Required Generations

[`PostgresDocumentMarkdownGenerationTests`](Tests/Integration/PostgresDocumentMarkdownGenerationTests.cs)
adds **11 methods / 26 Debug-only cases**, tagged `PreparedDocuments`, `Integration` and `PostgreSQL`.
It reuses the real synthetic hybrid producer and an isolated migrated database per case.
Coverage includes actual JSONB snapshots and unchanged archived raw receipt hashes, exact
source/job/plan reads and writes, eight-way immutable retries and changed review/profile writes,
database-owned timestamps, null/oversized receipt preflight and composite owner foreign keys.
Save checkpoints and observed advisory-lock waiters prove replacement/deletion in both admission
orderings, uncommitted output invisibility, receipt changes before locked recheck, lock cancellation
and post-insert rollback on failure/cancellation/source loss.

The fixture defaults to fresh full-chain migration. Its optional `InitialMigration` is used only
by the upgrade case: seed validated receipts on the preceding schema, migrate forward, prove those
receipts unchanged, then store/read a review-required generation. Only fixture-owned databases
are created, migrated and removed.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.PostgresDocumentMarkdownGenerationTests CasCap.Tests.Integration.DocumentMarkdownGenerationTests CasCap.Tests.Integration.DocumentHybridPreparationTests CasCap.Tests.Integration.PostgresDocumentPreparationIntegrationTests CasCap.Tests.Integration.PostgresDocumentPreparationPersistenceTests
```

The approved focused selection passed **149 cases**, zero failed/skipped: 26 new PostgreSQL
review-generation, 47 SQLite review-generation, 31 SQLite hybrid, 21 PostgreSQL hybrid-ledger and
24 PostgreSQL page-local persistence cases. Debug solution and restored Release server builds
pass with zero warnings/errors. The first native check exposed JSONB `string.Length` member
translation rather than text length; explicit Npgsql text conversion fixes it. SQLite keeps its
own supported expression, with both paths covered by the final run.

The local PostgreSQL service was stopped immediately afterward; pre-existing workloads/volumes
were preserved. The fixture-database count returned to its pre-run baseline without deleting
unknown databases. No Azure/paid calls, deployed database writes, real-document persistence,
management authorization, semantic/visual approval, embedding, activation or deployment occurred.

### Protected Document Review Management

[`AdministrationDocumentReviewTests`](Tests/Integration/AdministrationDocumentReviewTests.cs)
adds **21 methods / 60 Debug-only cases**, tagged `Integration` and `DocumentReviewManagement`.
It uses the actual host, authorization policies, antiforgery, typed browser client and production
review store with per-case SQLite and synthetic receipts. No identity-provider, Azure, paid-model,
PostgreSQL service or deployment is contacted.

Coverage proves cookie-only app assignments; 401/403 before reads/writes; no anonymous Development
bypass; required antiforgery; preview without insertion; immutable retry/profile behavior; source,
job, collection and tenant fences; rejected stale revisions and invalid structural decisions;
replacement/deletion revocation; explicit 501 without storage; and corruption as 500, not 400.
Forged JSON tenant/actor/location/state cannot replace trusted metadata or create approval.
Source lookup translation is checked offline with Npgsql, not by executing against PostgreSQL.
Catalogue coverage adds tenant/collection filters, bounded paging, current-job availability,
stored-generation discovery, revocation and no-store metadata without source URIs or plans.
The [chunk-review cases](Tests/Integration/AdministrationDocumentReviewTests.ChunkReview.cs)
prove explicit required members, exact revision bounds, snapshot-local exclusions, optional bounded
notes, authenticated attribution, optimistic concurrency/idempotency and unchanged preparation
artifacts. Snapshot deletion cascades to review records; source replacement/deletion revokes access.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.IntegrationTests.AdministrationDocumentReviewTests CasCap.IntegrationTests.AdministrationApiTests CasCap.IntegrationTests.AdministrationSignInTests CasCap.Tests.Integration.DocumentMarkdownGenerationTests
```

Before the catalogue additions, this selection passed **94 cases**, zero failed/skipped: 26 management, 47 existing
SQLite generation and 21 existing administration cases.

[`PostgresAdministrationDocumentReviewTests`](Tests/Integration/PostgresAdministrationDocumentReviewTests.cs)
adds **10 methods / 18 Debug-only cases**, also tagged `PostgreSQL`. Both management classes reuse
the [cookie-authenticated host](Tests/Integration/DocumentReviewWebApplicationFactory.cs), with
per-fixture native databases for this class. Synthetic preparation uses fake providers, not Azure.
Native coverage verifies JSONB source-plan materialization, eight-way immutable retries, profile
append, ownership fences, retained-receipt identity and source replacement/deletion revocation.
Observed advisory-lock waiters and save checkpoints verify 409 on source loss, post-insert
rollback on failure/source loss and a clean retry; missing/corrupt receipts remain 500.
The first native run exposed a GUID reader mismatch from synthetic lease defaults in a SQL
projection. Source lookup now materializes persisted fields before constructing those defaults;
offline translation and SQLite execution alone did not prove native reader compatibility.
The [native chunk-review cases](Tests/Integration/PostgresAdministrationDocumentReviewTests.ChunkReview.cs)
cover fresh schemas, upgrade with existing snapshots, JSONB round-trips, eight concurrent identical
writes, conflicting decisions and revocation while a real advisory-lock waiter is blocked.

After approval, start only the required [local PostgreSQL service](#persistence-test-services),
preserving existing workloads and volumes, then run:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.IntegrationTests.PostgresAdministrationDocumentReviewTests CasCap.IntegrationTests.AdministrationDocumentReviewTests CasCap.IntegrationTests.AdministrationApiTests CasCap.IntegrationTests.AdministrationSignInTests CasCap.Tests.Integration.DocumentMarkdownGenerationTests CasCap.Tests.Integration.PostgresDocumentMarkdownGenerationTests CasCap.Tests.Integration.PostgresDocumentPreparationPersistenceTests
```

Before the additional real-cookie replica case, this selection passed **157 cases**, zero failed/skipped:
13 native HTTP, 26 SQLite HTTP,
21 existing administration, 47 SQLite generation, 26 native generation and 24 native page-local
persistence cases. Full Debug solution and restored Release server builds passed with zero
warnings/errors. Fixture database counts returned to baseline; the task-started PostgreSQL service
was stopped and pre-existing services were preserved. Deployed migration and semantic/visual approval remain
separate gates. No paid call, embedding, activation or deployment occurred.

The additional real-cookie replica case replaces the header-driven handler with the actual cookie
handler in two independent hosts and stores their Data Protection keys in the same fixture database.
A synthetic authentication ticket protected by the first host is accepted by the second;
an antiforgery token/cookie from the first protects generation storage on the second, and the first
reads that generation. Anonymous reads still return 401. The case does not contact an OIDC issuer,
prove deployed ingress/callback configuration or create an authentication bypass in the application.
The focused native/SQLite review, existing replica and sign-in selection passes **54 cases**,
zero failed/skipped. Fixture database counts return to baseline and task-started PostgreSQL stops.

#### Catalogue And Review Browser Coverage

[`AdministrationDocumentUiTests`](Tests/Integration/AdministrationDocumentUiTests.cs) adds
**14 methods / 41 cases**, tagged `Integration` and `DocumentReviewManagement`.
The shared [static renderer](Tests/Integration/AdministrationComponentTestRenderer.cs) also serves
the existing definition UI tests. Synthetic HTTP verifies catalogue reads, escaped source names/
Markdown, heading context, separate/nonempty notes, exact snapshots without storage controls,
401/403/404/409/501 states, unsupported projections and honest empty-state wording.
Stored-snapshot rendering additionally checks selection controls, counts, filters, escaped exclusion
reasons and the explicit indexing block. These static checks do not prove checkbox clicks or dialogs.
The [selection-draft tests](Tests/Unit/DocumentChunkSelectionDraftTests.cs) add **two methods/cases**
for default inclusion, edits/restoration, explanations, counts and all-excluded decisions without I/O.

The focused selection of these three classes and the selection-draft class passed **121 cases**:
60 SQLite HTTP, 18 native PostgreSQL HTTP, 41 rendered UI and two pure draft cases, zero failed/skipped.
Fixture database counts returned to baseline; the pre-existing local PostgreSQL service was left
running. No Azure request, deployed migration, document approval or rollout occurred.

The focused offline selection passed **55 cases**, zero failed/skipped: 35 SQLite HTTP,
11 document UI and nine existing definition UI cases. Separately, all **15 native PostgreSQL**
review cases passed, including catalogue materialization, snapshot discovery and source supersession.
Fixture database counts returned to baseline and only task-started PostgreSQL was stopped.

Static rendering does not exercise clicks. An approved loopback-only browser smoke over actual
WebAssembly assets with synthetic API responses verified filtering/navigation, confirmation/cancel,
one explicit source-fenced/profile-preserving POST with antiforgery, exact-snapshot navigation,
409/503 outcomes with GET-only refresh, keyboard confirmation and narrow catalogue/review layouts.
Referenced scripts/styles returned 200; the temporary host was stopped.
This is not deployed OIDC acceptance or real-document browser quality approval.

Synchronization adds static-rendering coverage for enabled/mapped gates, progress/conflicts,
escaped safe failure output and catalogue independence, plus typed-client antiforgery and
no-retry checks. Synchronization-request confirmation and submission have not been browser-validated.

Stage-readiness coverage distinguishes a successful storage scan from paused processing and queued
jobs with/without pinned preview configuration, including explicit unknown-state labels and
collapsed technical identifiers. The exact focused selection passed **87 cases**, zero
failed/skipped: 39 synthetic UI, 35 SQLite review HTTP and 13 synchronization HTTP cases.
The full Debug solution builds with zero warnings/errors. A loopback browser smoke using synthetic,
read-only API fixtures verified the paused/setup-required combination, enabled/review-ready
wording, preserved Preview links, escaped filenames, collapsed details and keyboard expansion.
At 390px there was no horizontal page overflow with long filenames and 64-character identifiers.
No mutation requests were made. This is not authenticated deployed or real-document acceptance.

[`AdministrationDocumentSynchronizationTests`](Tests/Integration/AdministrationDocumentSynchronizationTests.cs)
adds **7 methods / 13 Debug-only cases**, tagged `Integration` and
`DocumentSynchronizationManagement`. The API-only host verifies administrator/cookie/antiforgery
boundaries, tenant isolation, safe progress, forged-body rejection, request coalescing and
disabled/unmapped/storage outcomes without resolving Blob/queue/processor transports.

The final exact offline selection passed **92 cases**, zero failed/skipped: synchronization HTTP,
document UI, SQLite review HTTP, ingestion composition, tenant-security startup and endpoint smoke
classes. The composition regression also proves repeated shared options registration keeps one
configured source rather than appending duplicates. Set `AppConfig__KeyVaultName=skip` as documented
above; these tests do not require PostgreSQL, Redis, Azure credentials or paid providers.

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.AdministrationDocumentUiTests CasCap.Tests.Integration.AdministrationDefinitionUiTests CasCap.IntegrationTests.AdministrationDocumentReviewTests
```

#### Retained Real-Document HTTP Acceptance

[`RetainedDocumentReviewAcceptanceTests`](Tests/Integration/RetainedDocumentReviewAcceptanceTests.cs)
adds **1 method / 1 Debug-only case**, tagged `Integration`, `PostgreSQL` and
`RetainedDocumentReview`. It is skipped unless `CASCAP_RUN_RETAINED_DOCUMENT_REVIEW=true` after
explicit approval of both migration and storage in the existing retained local acceptance ledger.
It never creates/drops that ledger, downloads sources or calls preparation providers.
The shared host uses the real authorization/antiforgery/controller/store/client pipeline with a
test administrator cookie stand-in, not deployed OIDC.

Supply these process-only values; keep the connection and private document paths outside Git:

| Variable | Required value |
| --- | --- |
| `ConnectionStrings__PostgreSql` | Existing retained ledger on IPv4 loopback, standard local PostgreSQL port, database name beginning `agentizr_acceptance_` |
| `CASCAP_RUN_RETAINED_DOCUMENT_REVIEW` | `true`, only for the separately approved run |
| `CASCAP_DOCUMENT_REVIEW_INPUT` | Absolute private original preparation-export directory with the two source PDFs |
| `CASCAP_DOCUMENT_REVIEW_EXPECTED` | Absolute verified projection directory with both page and chunk JSON exports |
| `CASCAP_DOCUMENT_REVIEW_ANNOTATIONS` | Absolute private directory with both schema-2 source-review files |
| `CASCAP_DOCUMENT_REVIEW_OUTPUT` | Fresh absolute private directory for generation exports and validation manifest |

The gate requires exactly two review-required jobs, two source records and eight validated receipts.
It permits only the additive `20261028120307_DocumentMarkdownReviewGenerations` pending migration,
or an already-upgraded ledger; other pending migrations fail before writes.
Source ordinals bind by original PDF hash, not tenant names or file-name guesses.
Before storage, preview pages and recomputed reviewed projections must match retained evidence.
Retries reuse immutable identities, reads match the expected projection, and a repeat run requires
the same retained generation pair without changing its storage timestamps or payloads.

After approval, start only [local PostgreSQL](#persistence-test-services), then run:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.IntegrationTests.RetainedDocumentReviewAcceptanceTests
```

The approved run passed **1 case**, zero failed/skipped, and applied only the review-generation
migration to the retained ledger. It persisted 23 manual chunks (maximum 302 tokens) and ten
vocabulary chunks (maximum 471), all unfragmented and exactly matching the verified projections
with the unchanged 512-token ceiling and 64-token overlap. All eight checked input files, eight
paid receipts, jobs and source records stayed unchanged; no page-local generation/chunk or runtime
run was created. A separate repeat run passed with no pending migration and the same two generation
IDs, payloads and timestamps. With opt-in disabled, the one case correctly skipped without a running
PostgreSQL service. **39 shared-host management regressions** also passed, zero failed/skipped.
Full Debug solution and restored Release server builds passed with zero warnings/errors.
Task-started PostgreSQL was stopped; disposable fixture counts returned to baseline and existing
workloads were preserved. These local review-required generations are not visual/semantic approval,
embeddings, retrieval eligibility, deployed migration or deployed authentication acceptance.

### PostgreSQL Prepared Generations

[`PostgresDocumentPreparationPersistenceTests`](Tests/Integration/PostgresDocumentPreparationPersistenceTests.cs)
adds **11 methods / 24 Debug-only cases**, tagged `PreparedDocuments`, `Integration` and `PostgreSQL`.
Each uses the existing fixture's fresh, migrated, disposable database and synthetic PDFs through
the production processor/store. Coverage includes eight-way generation/profile writes, immutable
retry deduplication, competing original digests, exact claim/source/plan fences, JSONB metadata,
database-owned timestamps and composite chunk ownership constraints. Save checkpoints and observed
advisory-lock waiters prove both admission/staging orderings for supersession/deletion, uncommitted
output invisibility and full rollback after insertion on failure, cancellation or deadline loss.

Start only the required [local PostgreSQL service](#persistence-test-services). Preserve existing
workloads and stop task-started services immediately afterward without removing volumes. Verify the
effective connection override; if the host publishes IPv4 loopback only, use `127.0.0.1` rather than
an unresolved/unpublished IPv6 `localhost` endpoint.
When constructing process-only overrides in PowerShell, use
`DbConnectionStringBuilder.set_ConnectionString` / `get_ConnectionString` and
`set_Item` / `get_Item`: PowerShell's dictionary adapter can treat property assignment as a
connection-string key instead of calling the intended setter.

```powershell
$env:AppConfig__KeyVaultName = 'skip'
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.PostgresDocumentPreparationPersistenceTests CasCap.Tests.Integration.PostgresDocumentPreparationIntegrationTests CasCap.Tests.Integration.DocumentPreparationPersistenceTests
```

The focused selection passed **81 cases**, zero failed/skipped: 24 PostgreSQL generation cases,
21 existing PostgreSQL hybrid-ledger cases and 36 SQLite persistence regressions. The Debug test
project built with zero warnings/errors. An earlier interrupted run was not counted as acceptance;
the complete rerun used the verified published IPv4 endpoint. PostgreSQL was stopped immediately
afterward; pre-existing containers and volumes were preserved.
No live source, paid provider, deployed database, definition activation, embedding or searchable
generation was involved. Hybrid review-only candidate receipts remain separate from these local
prepared generations.

### Durable Hybrid Preparation

These Debug-only tests are tagged `HybridPreparation`. They use synthetic configuration and PDFs,
an isolated SQLite database and fake HTTP/providers; they never start a live worker or call Azure.

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`DocumentHybridPreparationTests`](Tests/Integration/DocumentHybridPreparationTests.cs) | 17 | 31 | Real admission/PDF processing, pinned plans, selected original pages and source limits, single-submission receipts, exact claim/tenant fences, foreign keys, cancellation-time accounting, expired-intent blocking, managed GET resume, recorded-response reuse and review-only heading projection excluding annotations/usage |
| [`DocumentPreparationProviderTests`](Tests/Integration/DocumentPreparationProviderTests.cs) | 1 | 4 | Actual worker adapter, framework executor and SDK with fake HTTP: worker-local tenant credentials, selected physical-page authorization/schema, output ceiling, no ambient HTTP tenancy or definition re-resolution |
| [`DocumentPreparationModelVerifierTests`](Tests/Unit/DocumentPreparationModelVerifierTests.cs) | 3 | 12 | Bounded read-only ARM requests, exact account/endpoint/deployment/model/revision binding, redacted failure, oversized metadata and pre-cancellation |
| [`DocumentPreparationPlanTests`](Tests/Unit/DocumentPreparationPlanTests.cs) | 1 | 17 | Explicit budgets, distinct physical-page selections and supported stateless, secret-free, exact-schema Azure Responses authorization |
| [`DocumentPreparationCandidateTests`](Tests/Unit/DocumentPreparationCandidateTests.cs) | 2 | 12 | Markdown whitespace/entity projection without receipt mutation, encoded preview content, separate/collapsed changes, conditional page uncertainties and no inline usage |
| **Hybrid subtotal** | **24** | **76** | Local implementation proof, not PostgreSQL concurrency, Azure access, document fidelity or invoice reconciliation |

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait Category=HybridPreparation
```

All 76 cases passed within the 133-case offline selection described above. The earlier focused run
including the built-in template and administration definition UI regressions passed **87 cases**,
zero failed/skipped. These validate
the `PdfReconciliationV2` prompt and template discovery without changing installed definitions.
The actual SDK response-observer regressions also
exercise receipt observation before malformed/incomplete output is rejected. Missing token usage
stays unknown, and no test enables ingestion, activates a definition or replays a paid request.
The shared Azure layout transport has its own fake-handler suite. Markdown passthrough,
pricing estimates, protected document-management preview UI and operator reconciliation are not
implemented by this selection.

The earlier combined PostgreSQL/SQLite persistence and offline hybrid selection passed **144 cases**,
zero failed/skipped: the 81 persistence cases above plus the then-current 63 hybrid cases. The paid acceptance
case and Redis host handover were not selected. The retained paid ledger was preserved, and
task-started PostgreSQL was stopped immediately after the run.

### Live Production Preparation Acceptance

[`DocumentPreparationLiveAcceptanceTests`](Tests/Integration/DocumentPreparationLiveAcceptanceTests.cs)
has **one Debug-only case**, tagged `LiveDocumentPreparation`, `Integration` and `PostgreSQL`.
It is skipped unless `CASCAP_RUN_DOCUMENT_PREPARATION_ACCEPTANCE=true`; enabling it is not spending
approval. Obtain explicit approval for each source/model/page/request/token/deadline scope first.
It calls the production worker, provider, built-in template and durable ledger, not another
inference client. It never starts a deployed host, consumes shared jobs or changes live definitions.

Required process-local inputs:

| Variable | Contract |
| --- | --- |
| `CASCAP_DOCUMENT_ACCEPTANCE_ROOT` | Application configuration root, using the normal configuration/user-secrets/Key Vault identity chain |
| `CASCAP_DOCUMENT_ACCEPTANCE_OUTPUT` | New absolute private review directory; existing evidence is never overwritten |
| `CASCAP_DOCUMENT_ACCEPTANCE_PAGES` | JSON mapping of the two explicitly approved tenant bindings to three original physical pages each |
| `CASCAP_DOCUMENT_ACCEPTANCE_METADATA` | Read-only ARM-discovered deployments/endpoints, layout endpoint and explicitly approved model/revision; exactly one policy-authorized target must match |
| `ConnectionStrings__PostgreSql` | Fresh `agentizr_acceptance_` database on `127.0.0.1:5432`; never a deployed or disposable fixture database |

Supply trusted tenant/collection overrides explicitly if local source entries contain only
container/topic settings. Credential-only provider registrations do not select a model: the local
preparation definition binds exact deployment metadata within the existing endpoint policy.
Do not carry the offline Key Vault skip sentinel into this live configuration.

The case preflights both complete originals and model authorization before any paid intent.
It permits at most two managed submissions and six page requests, with 8,192 output tokens/page,
120 seconds/request and 900 seconds/document. It retains the local database on success or failure;
inspect receipts instead of automatically rerunning against a new database. Stop task-started
PostgreSQL immediately after use, preserving its volume and pre-existing workloads.

The separately approved run on 2026-10-28 passed: two unchanged original PDFs, six original physical
pages, two `ReviewRequired` jobs and eight validated operations, with zero generations, chunks or
generic agent-run history. The existing PAYG `gpt-5.5` revision `2026-04-24` returned **23,536 input /
6,275 output / 29,811 total tokens**. Managed receipts record six returned physical-page units;
neither this count nor native tokens establish a currency total or invoice reconciliation.
Private three-panel original/draft/agent previews and usage exports were saved. The operator accepted
the six sampled transcriptions with qualified minor vocabulary Markdown discrepancies; this is not
full-document or diagram acceptance. This local-identity result does not establish deployed inference
access, queue-to-answer behavior or searchable activation.
The review renderer keeps transcription in its own element, hides empty review annotations, labels
nonempty **Page Uncertainties** and collapses **Review changes (not document content)**. Native usage
stays in the separate usage exports/ledger, never in the Markdown. The saved preview was refreshed
offline with this presentation and whitespace cleanup; neither raw receipts nor usage exports were
changed, and no new provider call was made.

On a future separately authorized live run, the exporter also projects validated candidate Markdown
through the shared heading chunker, writes `source-N.chunks.json` and appends a collapsed
review-only chunk panel. Its token counts describe embedding-input budgets, not provider usage.
The retained six-page artifact has not been regenerated with these exports. A structured unit
exceeding the hard ceiling fails for review; inspect retained receipts rather than replaying
paid preparation to bypass that rejection.

### PostgreSQL And Redis Preparation Acceptance

These Debug-only cases use the real local providers, not SQLite or a fake leadership runner.
They are tagged `PreparationProviders`, `Integration` and `PostgreSQL`; the host case also has `Redis`.

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`PostgresDocumentPreparationIntegrationTests`](Tests/Integration/PostgresDocumentPreparationIntegrationTests.cs) | 10 | 21 | Fresh migrations, JSONB plans/results/native usage, eight-way claims/reservations, cancellable advisory-lock contention, immutable competing receipts, database-time deadlines, composite ownership constraints, stale/deleted/superseded claim fences, ambiguous-intent blocking and actual worker receipt resume without resubmission |
| [`DocumentIngestionHostIntegrationTests`](Tests/Integration/DocumentIngestionHostIntegrationTests.cs) | 1 | 1 | Two real ingestion-only hosts, health/status-only endpoints, complete worker/provider resolution, shared Redis contention, cancellation and graceful handover; only Azure queue delivery is substituted and processing stays disabled |
| **Provider subtotal** | **11** | **22** | Local provider acceptance, not Azure access, real-document fidelity, renewal-loss safety or deployment acceptance |

Use the [local persistence services](#persistence-test-services). Verify that the final
`ConnectionStrings:PostgreSql` and `ConnectionStrings:Redis` overrides target only the approved
local services before running; the fixture creates/drops its own random databases. Disable Key Vault
bootstrap through its actual sentinel, not the derived read-only `IsKeyVaultEnabled` property:

```powershell
$env:AppConfig__KeyVaultName = 'skip'
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=PreparationProviders' 'Category=HybridPreparation'
```

The earlier combined selection passed **75 cases**, zero failed/skipped: 22 provider-backed cases
and the then-current 53 offline hybrid regressions. The hybrid selection now has 76 cases; the
Redis host case was not rerun as part of the later PostgreSQL/page-selection work.
Provider requests are synthetic, so no Azure calls or paid
operations occur. Polling receipts resume without another submission, received layout/model JSONB
receipts are reused, unknown submissions stay blocked despite late receipts, and successful candidates
remain `ReviewRequired` without chunks or searchable activation. Native token counts round-trip exactly;
these checks neither estimate currency nor reconcile an invoice.

Agentizr owns the [preparation definition](../CasCap.App.Server/Constants/DocumentPreparationAgentTemplate.cs);
tests consume that contract rather than a second prompt or inference client. Its admin-catalogue
template is unconfigured: select a tenant-authorized Azure OpenAI provider/model and explicitly
publish/activate a tenant-local version before binding a source. The worker pins that immutable
version and verified deployment revision.

Production PdfPig parsing, page copying, bounded local preparation and persistence retain their
regression tests. Document Intelligence HTTP behavior belongs to the shared Azure transport suite;
the hybrid tests exercise it through the production provider boundary. Real-document semantic
acceptance and deployed assigned-administrator browser checks remain separate gates in
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#built-in-document-preparation-agent).

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 122 | ASP.NET Core host protocols, synthetic definition-page rendering, startup security, PostgreSQL persistence and Redis payload caching |
| `Generation` | 7 | Credential-free definition-version suggestions with a fixed clock |
| `Agent Runtime` | 54 | Runtime orchestration, tools, metrics, evaluation, persistence, caching, locking and compaction |
| `Document Preparation` | 3 | Secret-free preparation template, editor constraint preservation and stateless runtime execution |
| `HybridPreparation` | 76 Debug-only | Bounded worker/runtime, selected original pages, Markdown/review separation, review-only heading projection, paid receipts/recovery, profile authorization and fake read-only model metadata; 35 cases also tagged `Integration` |
| `MarkdownChunking` | 95 Debug-only | Synthetic heading hierarchy, semantic units, reviewed exclusions/structural decisions, cell/source/page spans, identity and complete-input token bounds |
| `OfflineDocumentReview` | 14 Debug-only | Thirteen synthetic export/coverage checks and one separately approved retained-artifact projection without providers or databases |
| `LiveDocumentPreparation` | 1 Debug-only | Separately approved real-source/provider acceptance with a retained local ledger; disabled by default |
| `RetainedDocumentReview` | 1 Debug-only | Separately approved migration and protected HTTP storage of reviewed real-document projections in the retained local ledger; disabled by default |
| `DocumentReviewManagement` | 121 (78 Debug-only) | 60 SQLite HTTP, 18 native PostgreSQL HTTP, 41 synthetic UI and two pure draft cases; all but the draft cases also `Integration` |
| `DocumentSynchronizationManagement` | 13 Debug-only | API-only administrator authorization, safe status and coalesced requests; also `Integration` |
| `DocumentSynchronization` | 23 Debug-only | SQLite/native PostgreSQL inventory, repair, deletion, migration and takeover contracts |
| `PreparationProviders` | 22 Debug-only | Real isolated PostgreSQL ledger/recovery and two-host Redis startup/handover; all also `Integration` and `PostgreSQL`, one also `Redis` |
| `Tenant Isolation` | 38 | Cross-tenant state, JWT permissions, caller mappings, administrator assignment, MCP and provider credentials and identity isolation |
| `PostgreSQL` | 25 | Definitions, durable state, run history and shared Data Protection keys against local Compose PostgreSQL |
| `Redis` | 8 | Revision-qualified state caching against local Compose Redis; also tagged `Integration` and `PostgreSQL` |
| `Local Agent Runtime` | 1 | Explicitly enabled smoke test against a complete running runtime |

The category inventory above covers the runtime baseline. Debug adds one case to each of
`Integration` and `PostgreSQL` for the runtime logging soak; role/ingestion, PDF preparation and
Blob download/prepared-document, review management, hybrid and provider-acceptance categories and counts are listed
separately above.

## Skipped Tests

| Condition / source-declared reason | Cases affected |
| --- | ---: |
| Live runtime tests require `CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS=true` outside CI | 1 |
| Once enabled, missing/invalid live base address, agent name or requested certificate-auth settings | 1 (same case) |
| Live preparation requires separate approval and `CASCAP_RUN_DOCUMENT_PREPARATION_ACCEPTANCE=true` | 1 Debug-only |
| Retained-document review requires explicit approval and `CASCAP_RUN_DOCUMENT_OFFLINE_REVIEW=true` | 1 Debug-only |
| Retained-ledger HTTP storage requires explicit migration/storage approval and `CASCAP_RUN_RETAINED_DOCUMENT_REVIEW=true` | 1 Debug-only |

These are conditional skips, not observed outcomes. PostgreSQL/Redis cases do not automatically
skip without their services: start the local Compose services or exclude both categories.

## Persistence Test Services

From the repository root:

```powershell
docker compose up -d --wait postgres redis
dotnet test --project src\CasCap.App.Server.Tests --configuration Release --filter-trait 'Category=PostgreSQL'
docker compose stop postgres redis
```

Fixtures create/drop only their own random test databases and use tenant-qualified Redis keys.
Localhost connection settings use the shared `Test`-environment loader with optional
`appsettings.Test.json`, test-local overrides, user secrets and final environment-variable overrides.
Compose credentials are loaded
from `.env.example` and optional gitignored `.env`, not embedded in test fixtures.
They never start ad hoc containers, touch the development database or flush Redis. The separately
gated retained-document acceptance is the explicit exception to disposable per-run databases:
it preserves the operator-selected local paid ledger and its review generations. See
[Compose definition](../../docker-compose.yml) and
[persistence setup](../../docs/persistence.md#local-development-and-tests) for startup and cleanup.

## Live Agent Runtime Smoke Test

The smoke test reaches a complete running runtime through `CasCap.AgentRuntime.Client`. It never runs
in CI and requires explicit local opt-in:

```powershell
$env:CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS = 'true'
$env:CASCAP_AGENTRUNTIME_LIVE_BASE_ADDRESS = 'https://runtime.example.com/'
$env:CASCAP_AGENTRUNTIME_LIVE_AGENT_NAME = '<tenant-agent-name>'
dotnet test --project src/CasCap.App.Server.Tests/CasCap.App.Server.Tests.csproj --filter-trait 'Category=Local Agent Runtime'
```

For certificate authentication, also set `CASCAP_AGENTRUNTIME_LIVE_SCOPE`,
`CASCAP_AGENTRUNTIME_LIVE_TENANT_ID`, `CASCAP_AGENTRUNTIME_LIVE_CLIENT_ID`, and
`CASCAP_AGENTRUNTIME_LIVE_CERTIFICATE_PATH`. The certificate path points to a local combined PEM file;
its content is read only at runtime and is never logged or persisted. Omit the authentication variables
only when testing a local runtime with authentication disabled.
`CASCAP_AGENTRUNTIME_LIVE_TIMEOUT_SECONDS` optionally replaces the default 300-second timeout.

## Layout

```text
Tests/
├── AgentDefinitionTestData.cs
├── DocumentHybridPreparationTestData.cs
├── DocumentIngestionTestData.cs
├── DocumentPreparationReviewRenderer.cs
├── DocumentPreparationTestData.cs
├── ScriptedEvaluationAgentFactory.cs
├── Integration/
│   ├── AdministrationApiTests.cs
│   ├── AdministrationComponentTestRenderer.cs
│   ├── AdministrationDocumentReviewTests.cs
│   ├── AdministrationDocumentSynchronizationTests.cs
│   ├── AdministrationDocumentUiTests.cs
│   ├── AdministrationDefinitionUiTests.cs
│   ├── AdministrationReplicaTests.cs
│   ├── AdministrationSignInTests.cs
│   ├── AdministrationSignInWebApplicationFactory.cs
│   ├── AdministrationTestClient.cs
│   ├── AgentizrMcpEndpointTests.cs
│   ├── AgentizrWebApplicationFactory.cs
│   ├── AgentDefinitionAdministrationProtocolTests.cs
│   ├── AgentDefinitionAdministrationWebApplicationFactory.cs
│   ├── AgentRuntimeProtocolTests.cs
│   ├── AgentSessionConflictTests.cs
│   ├── EndpointSmokeTests.cs
│   ├── DocumentIngestionInboxTests.cs
│   ├── DocumentIngestionLeadershipTests.cs
│   ├── DocumentIngestionHostIntegrationTests.cs
│   ├── DocumentHybridPreparationTests.cs
│   ├── DocumentMarkdownGenerationTests.cs
│   ├── DocumentPreparationLiveAcceptanceTests.cs
│   ├── DocumentPreparationPersistenceTests.cs
│   ├── DocumentPreparationProviderTests.cs
│   ├── DocumentReviewWebApplicationFactory.cs
│   ├── HostIntegrationCollection.cs
│   ├── McpAgentizrWebApplicationFactory.cs
│   ├── PostgreSqlFixture.cs
│   ├── PostgresAgentDefinitionStoreIntegrationTests.cs
│   ├── PostgresAdministrationDocumentReviewTests.cs
│   ├── PostgresDocumentPreparationIntegrationTests.cs
│   ├── PostgresDocumentMarkdownGenerationTests.cs
│   ├── PostgresDocumentPreparationPersistenceTests.cs
│   ├── AgentRunHistoryTests.cs
│   ├── PostgresAgentStateTests.cs
│   ├── RedisStateFixture.cs
│   ├── RetainedDocumentReviewAcceptanceTests.cs
│   └── TenantSecurityStartupTests.cs
├── Local/
│   ├── LiveAgentRuntimeSmokeTests.cs
│   ├── LiveAgentRuntimeTestSettings.cs
│   ├── LocalAgentRuntimeFactAttribute.cs
│   └── LocalAgentRuntimeTestEnvironment.cs
└── Unit/
    ├── AdministrationAccessTests.cs
    ├── AdministrationSetupServiceTests.cs
    ├── DefinitionVersionSuggestionTests.cs
    ├── AgentDefinitionEvaluatorTests.cs
    ├── AgentRuntimeAuthorizationTests.cs
    ├── AgentRuntimeBuiltInToolsTests.cs
    ├── AgentRuntimeMetricsTests.cs
    ├── AuthenticatedTenantContextTests.cs
    ├── AgentExecutionCoordinatorTests.cs
    ├── AgentSessionLockTests.cs
    ├── CachedAgentDefinitionStoreTests.cs
    ├── ConfigurationMcpCredentialStoreTests.cs
    ├── ConfigurationProviderCredentialStoreTests.cs
    ├── CommonAgentExecutorTests.cs
    ├── DocumentPreparationAgentTemplateTests.cs
    ├── DocumentPreparationCandidateTests.cs
    ├── DocumentPreparationOfflineReviewTests.cs
    ├── DocumentIngestionAdmissionTests.cs
    ├── DocumentIngestionConfigTests.cs
    ├── DocumentMarkdownChunkingTests.cs
    ├── DocumentMarkdownReviewTests.cs
    ├── DocumentMarkdownStructureTests.cs
    ├── DocumentPreparationModelVerifierTests.cs
    ├── DocumentPreparationPlanTests.cs
    ├── FeatureConfigTests.cs
    ├── InMemoryDistributedCache.cs
    ├── PostgresAgentDefinitionStoreTests.cs
    ├── SessionCompactionConfigTests.cs
    └── TenantStateIsolationTests.cs
```

## Run

Use the .NET 10 SDK and repository-selected Microsoft.Testing.Platform runner. Run from the
repository root only when you intend to execute tests. Debug references need adjacent
`CasCap.Common` and `CasCap.Api.Azure` checkouts; Release uses published dependencies.

Select the credential-free in-memory protocol category without PostgreSQL or local cases:

```powershell
dotnet test --project src\CasCap.App.Server.Tests\CasCap.App.Server.Tests.csproj --configuration Release --filter-trait 'Category=Integration' --filter-not-trait 'Category=PostgreSQL' --filter-not-trait 'Category=Redis'
```

For the full project, start the Compose test services first:

```powershell
dotnet test --project src\CasCap.App.Server.Tests\CasCap.App.Server.Tests.csproj --configuration Release
```

To run all offline coverage without Docker or live runtime access:

```powershell
$env:AppConfig__KeyVaultName = 'skip'
dotnet test --project src\CasCap.App.Server.Tests\CasCap.App.Server.Tests.csproj --configuration Debug --filter-not-trait 'Category=PostgreSQL' 'Category=Redis' 'Category=Local Agent Runtime'
```

Observed after document-harness cleanup: **421 passed, zero failed/skipped**. The execution-library
offline selection also passed **145 cases**; the shared Azure layout transport passed **330 cases**
across .NET 8/9/10. The full Debug and Release solutions build without warnings/errors.
PostgreSQL, Redis and live/provider tests were excluded, not validated.

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.App.Server` | Application under test |
| `CasCap.AgentRuntime.Client` | Typed client exercised against the in-memory host |
| `CasCap.Common.Caching` | Redis contracts used by the production state implementations |
| `Microsoft.EntityFrameworkCore.Sqlite` | Credential-free relational definition-store tests |
| Root Compose `tests` profile | Reusable PostgreSQL/Redis services, with test-owned databases and key namespaces |
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |
