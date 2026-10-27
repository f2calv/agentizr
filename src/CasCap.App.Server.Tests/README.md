# CasCap.App.Server.Tests

Unit, in-memory protocol, PostgreSQL/Redis integration and opt-in live-runtime/document tests for
`CasCap.App.Server`. Most cases are credential-free; PostgreSQL and Redis cases require the Compose tests profile and the
live cases require explicitly configured deployment credentials.

## Purpose

Protocol tests launch the real application through `WebApplicationFactory<Program>` with scripted
execution or evaluation substitutes. Unit tests cover orchestration, authentication, metrics and
state; PostgreSQL integration cases exercise isolated per-fixture databases on reusable Compose services.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `EndpointSmokeTests` | 5 | 9 | Verifies status and health endpoints, the frontend shell without capturing API routes, disabled MCP, all 19 named runtime controller actions and inferred API response metadata |
| `AdministrationApiTests` | 7 | 8 | Verifies open-Development app, agent and provider discovery, guarded activation conflicts, enum names, editor drafts that keep unedited fields, required antiforgery tokens and redirect-free rejection of unknown apps |
| `AdministrationSignInTests` | 6 | 13 | With sign-in enabled, verifies 401/403 per-app assignment, assigned-app listing, session description, administrator audit identity, logout antiforgery, local-only return URLs and the setup page for ignored forwarded HTTPS |
| `AdministrationReplicaTests` | 1 | 1 | Against real PostgreSQL: an antiforgery token issued by one host replica is accepted by another through the shared Data Protection key ring |
| `AgentizrMcpEndpointTests` | 3 | 3 | Verifies MCP discovery of only the read-only status tool, its structured result, and `Origin` rejection |
| `AgentRuntimeProtocolTests` | 9 | 12 | Verifies typed-client execution, streaming, DTO/JSON validation problems, session controls and instruction disclosure |
| `AgentDefinitionAdministrationProtocolTests` | 13 | 23 | Verifies publication Location, evaluation/activation, rollback, deactivation, audit attribution, policy/credential rejection, conflicts and bounded query binding |
| `TenantSecurityStartupTests` | 8 | 12 | Verifies anonymous rejection, policy separation on all runtime and administration controller actions, the setup page shown instead of an unusable frontend, required JWT/PostgreSQL and optional Redis in Production |
| `AgentExecutionCoordinatorTests` | 7 | 12 | Verifies execution/session orchestration, stateless isolation and effective read policy, all six rejected session mutations and the actual HTTP 409 problem response |
| `DocumentPreparationAgentTemplateTests` | 3 | 3 | Pins the frozen prompt, bounded secret-free template, editor preservation and actual runtime execution without discovery tools, time context, overrides, persisted sessions or content history |
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
| `RuntimeLoggingTests` (Debug only) | 1 | 1 | Real PostgreSQL publications change the Serilog pipeline and forwarded logs without losing a process-cache sentinel; category overrides remain intact |
| **Runtime baseline subtotal** | **140** | **209** | Excludes Debug-only logging; role/ingestion additions are listed separately below |

The runtime logging soak is Debug-only. Run that case with:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.RuntimeLoggingTests
```

Observed targeted Debug results: the logging soak case passed against real PostgreSQL, and all
nine existing endpoint smoke cases passed with runtime configuration disabled. This does not claim
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
| `DocumentIngestionConfigTests` | 5 | 13 | Synthetic source/queue configuration, canonical HTTPS endpoints, visibility and exact processing deadline boundaries |
| `DocumentIngestionAdmissionTests` | 9 | 20 | Commit-before-ack, infrastructure/argument failures, cancellation, malformed envelopes, source isolation and sequencer normalization |
| `DocumentIngestionInboxTests` | 16 | 19 | SQLite admission, disabled/missing processors, terminal outcomes, exact retry delay/attempt limit, expired and replaced claims, supersession, cancellation, deadline cleanup and unknown faults |
| `DocumentIngestionLeadershipTests` | 2 | 3 | Fake-runner ownership, cancellation on loss and processing resolution only inside ownership when enabled |
| `DocumentIngestionCompositionTests` | 1 | 4 | Validated real PDF-processor DI, disabled/enabled gates, explicitly removed processor rejection and test substitution; no transport starts |
| **Role/ingestion subtotal** | **36** | **70** | All but the role-parser class are Debug-only |

Run the focused credential-free selection from the repository root:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class '*FeatureConfigTests' '*DocumentIngestion*'
```

The database/DI fixtures are under `Integration/` and use synthetic, test-owned setup rather than
the live-service `TestBase`; no Azure, PostgreSQL or Redis connection is made.
`Roles` covers the 11 parser cases; `Ingestion` covers the remaining 59, including 26 also tagged
`Integration`. The processing tests use a manual clock/timer, not retry-duration sleeps.
Neither a fake ownership callback nor SQLite validates actual Redlock renewal,
PostgreSQL advisory-lock concurrency, Azure queue encoding/leases/poison delivery, migration process
exit or worker-only/combined host routes.

Observed current combined Debug run: **178 passed, zero failed/skipped**: the 70 cases above, nine
existing endpoint cases, 42 preparation cases, 21 download cases and 36 staged-persistence cases.
This is not a
full-suite or live-provider result. See
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#poc-limits-and-deliberately-deferred-polishing)
for the remaining acceptance gates. The real processor is exercised with synthetic streams and
SQLite; no live-provider race test or blob-to-answer flow is claimed.

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
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class '*DocumentPreparationPersistenceTests'
```

All 36 cases passed within the 178-case combined run. The full Debug solution builds cleanly;
offline EF checks report no pending model changes and the additive preparation migration SQL was
reviewed. No migration was applied. SQLite does not prove PostgreSQL locks/races, live identity,
JSONB storage, embeddings, searchable activation or cleanup/retention behavior.

### Document Quality And Live PDF Preparation

| Class | Methods | Cases | Scope |
| --- | ---: | ---: | --- |
| [`DocumentLayoutQualityTests`](Tests/Unit/DocumentLayoutQualityTests.cs) | 5 | 8 | Independent synthetic manual/vocabulary passage expectations, German umlauts, column order, table-row associations, semantic splits despite lossless token coverage and reading-order checks |
| [`DocumentQualityReviewWriterTests`](Tests/Unit/DocumentQualityReviewWriterTests.cs) | 4 | 12 | Synthetic preview encoding/fencing, exact original bytes, page/view navigation markup, profile/chunk labels, safe filenames, refusal to overwrite and review-only heading spacing without text deletion/reordering |
| [`DocumentLayoutReviewTests`](Tests/Unit/DocumentLayoutReviewTests.cs) | 16 | 32 | Section-band/column order, dictionary headword/example grouping and continuations, detached/Unicode list markers, example/compact-list boundaries versus adjacent wrapped prose, paragraph gaps, split-heading reconstruction, contiguous bold runs, one/two-column contents with leading/trailing references, repeated versus unique/side margins, contextual punctuation/source-markup escaping, blank pages, cancellation, rejected missing/duplicate blocks and private structured exports |
| [`DocumentIntelligenceQualityClientTests`](Tests/Unit/DocumentIntelligenceQualityClientTests.cs) | 13 | 25 | Offline fake transport: exact submission options, no POST replay, safe polling origin, strict page/span/JSON contracts, response cap, cancellation and receipt/result reuse including ambiguous submissions and interrupted polling |
| [`DocumentIntelligenceBenchmarkReviewTests`](Tests/Unit/DocumentIntelligenceBenchmarkReviewTests.cs) | 1 | 1 | Synthetic managed/local exports escape source markup, retain exact Markdown and reject changed originals on resume |
| [`OpenAIDocumentQualityTests`](Tests/Unit/OpenAIDocumentQualityTests.cs) | 10 | 28 | Offline PDF-only/hybrid request contracts, copied-page isolation, no replay after failure, cached versioned/unversioned model labels, rejection of other models/revisions, prompt/schema/token/page bounds, cancellation, lexical differences and encoded exports |
| [`LiveDocumentQualityTests`](Tests/Integration/LiveDocumentQualityTests.cs) | 2 | 3 | Explicitly enabled, read-only Blob download/preparation/coverage, optional independently reviewed semantic acceptance and separately gated private review output |
| [`LiveDocumentIntelligenceQualityTests`](Tests/Integration/LiveDocumentIntelligenceQualityTests.cs) | 1 | 1 | Separately approved, charged managed-layout benchmark on two cached hash-pinned PDFs and exactly six physical pages; not part of the read-only Blob category |
| [`LiveOpenAIDocumentQualityTests`](Tests/Integration/LiveOpenAIDocumentQualityTests.cs) | 2 | 2 | Separately gated six-request PDF-only/hybrid trial and a distinct non-generative app-identity access check; both verified on the alternative subscription, semantic review pending |

Run the credential-free layout group from the repository root:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=DocumentQuality'
```

The eight layout cases passed within an 84-case focused offline run covering preparation,
downloads, composition and endpoint smoke checks. Key Vault was explicitly disabled for that
offline host run using `AppConfig__KeyVaultName=skip`. This is not a complete suite run.
The full Debug solution also built with zero warnings/errors.
The synthetic tests deliberately characterize two limitations: content-stream order can interleave
independent columns, while generic spatial column detection can break table-row associations.
Neither extractor is universally correct. The WinAnsi fixture covers umlauts without depending on
an installed font; PdfPig's Standard14 fixture writer does not support those characters.

The live cases use the standard `Test` configuration chain and the application's shared
[Key Vault bootstrap](../CasCap.App.Server/Extensions/HostApplicationBuilderExtensions.cs).
An optional `DocumentQuality:LocalConfigurationPath` loads an existing absolute private JSON path,
then reapplies repository user secrets and environment overrides before adding Key Vault with the
same secret selection and identity as the application. The private file is read in place, not
copied into output. Certificates remain in the existing credential store.

Live tests require explicit approval and opt-in:

```powershell
$env:DocumentQuality__LocalConfigurationPath = (Resolve-Path .\appsettings.Local.json).Path
$env:DocumentQuality__LiveEnabled = 'true'
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=DocumentQualityLive' --output Detailed
```

Do not carry the offline `AppConfig__KeyVaultName=skip` override into this command. Configure
authorized containers under `CasCap:DocumentIngestionConfig:Sources`. The fixture assigns synthetic
tenant/collection bindings only in memory; it does not complete production ownership mappings.
It registers no host, worker, queue, database or model client. Only Blob list/properties/conditional
read operations are used; no resources or documents are created, changed or deleted.

By default, discovery inspects at most 50 Blob entries and prepares at most two PDFs per source.
Set `DocumentQuality:Sources:<index>:BlobName` to select one exact PDF instead. Both extraction
strategies consume bounded, hash-identical bytes at the captured ETag. The live test checks the
production pipeline's token counts, page attribution, exact span coverage, no-text-page reporting and Unicode
replacement characters. Test-only nearest-neighbour/Docstrum/reading-order analysis compares
normalized text; disagreement is not proof that either strategy is better. It is not a Markdown
converter or a production algorithm change.

Observed read-only execution prepared a PDF from each of the two configured sources and verified
complete chunk coverage. Content-order and spatial extraction disagreed on both documents.
The live selection passed one case and skipped the separate semantic case: no independently reviewed passage/order oracle has
been supplied. A successful preparation is not semantic or retrieval-quality acceptance.

For semantic acceptance, independently inspect each original PDF, then supply the following
**per-source configuration fragment** through private configuration. Replace every example value
and repeat for every configured source; never generate the expected passages from extractor output.

```json
{
  "DocumentQuality": {
    "SemanticEnabled": true,
    "Sources": {
      "0": {
        "BlobName": "reference.pdf",
        "SourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
        "ExpectedPageCount": 1,
        "RequiredPassages": ["WARNING: Disconnect power."],
        "OrderedPassages": ["Maintenance", "Disconnect power"]
      }
    }
  }
}
```

The semantic case pins the reviewed bytes/page count, requires passages to remain together within
a chunk and checks independent document reading-order anchors. Assertions withhold source text,
names, hashes and endpoints. Known provider failures expose only safe categories/status codes.
Without preview opt-in, source buffers are memory-only. Owned configuration, service providers, log providers and streams
are disposed. Cancellation is checked between synchronous parser/layout calls; the five-minute
cooperative processing budget is not a hard parser sandbox or a bound on Key Vault bootstrap.

This does not validate Event Grid admission, PostgreSQL persistence/races, production tenant
bindings, Markdown reconstruction, OCR, embeddings or retrieval quality. Keep ingestion disabled.

#### Private Review Previews

After explicit approval, enable `DocumentQuality:PreviewEnabled` as well as `LiveEnabled` and
configure an absolute `DocumentQuality:PreviewDirectory` outside Git worktrees. The generator
rejects paths with a Git worktree ancestor and creates a unique `review-<id>` child. The output
directory must be private and operator-controlled; inherited filesystem permissions are not
encryption or a security boundary against other local users.

```powershell
$env:DocumentQuality__LocalConfigurationPath = (Resolve-Path .\appsettings.Local.json).Path
$env:DocumentQuality__LiveEnabled = 'true'
$env:DocumentQuality__PreviewEnabled = 'true'
$env:DocumentQuality__PreviewDirectory = Join-Path $env:TEMP 'private-document-review'
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=DocumentQualityPreview' --output Detailed
```

The [review writer](Tests/DocumentQualityReviewWriter.cs) saves the bounded, ETag/hash-verified
original PDF, a side-by-side HTML review and a Markdown review report for each selected sample,
using generic source/sample filenames. An index links the bundles. HTML offers page selection
and content-order, exploratory spatial-order and current-token-chunk views, with source/profile
pins and chunk offsets/token counts. Raw source text is HTML-encoded or fenced in Markdown;
the HTML uses no external assets and restricts network access through its content policy.
Files use create-new semantics and are never silently overwritten.

Content-order and spatial text views add blank lines around uppercase numbered heading
candidates to make comparison easier. This is display-only spacing, not semantic heading
detection or hierarchy reconstruction; chunk text and its original offsets remain unchanged.
Running headers/footers and reading-order defects remain visible rather than being hidden by
preview formatting.

Open the index in a local desktop browser; if embedded PDF viewing is unavailable, open the
original PDF separately. Compare headings/paragraphs, procedures/warnings, table associations,
headword/forms/examples, columns and cross-page continuations against the original. The
Markdown is a review report, not semantic PDF-to-Markdown reconstruction. Neither preview
implements the planned section-based or entry-based profile.

#### Structured Layout Candidates

The test-only [layout review engine](Tests/DocumentLayoutReview.cs) adds `LayoutReviewV6`
section-oriented and entry-row-oriented candidates to the same private HTML review. Each
source also exports `.sections.md`, `.entries.md` and `.layout.json`, pinned to the original
byte hash. JSON records retained/excluded word counts and per-view line rectangles in PDF
bottom-left coordinates. These are extraction prototypes, not production chunking profiles.

HTML opens the section candidate by default when available; baseline-only reviews still open
content-order text. The header's **Refined section Markdown** link opens `.sections.md`.
The separate `.md` report is labelled **Original baseline Markdown** and deliberately retains
the original extraction defects and token chunks for comparison; it is not the refined output.

- Section candidates rebuild physical baseline rows from captured Docstrum words rather than
  sorting potentially multi-baseline block text by horizontal position. Left-side bold labels
  define candidate section bands; evidenced central gutters permit column-first reading within
  each band, preserving list continuations before the next section. Full-width rows inhibit splitting.
- Geometry-based paragraph gaps, embedded bold-style metadata and conservative numbered/large-bold
  heading candidates separate prose and headings. Same-row heading fragments are rendered together;
  headings omit redundant bold delimiters, and body text groups consecutive bold words.
- Bands containing at least two equation rows or an alphabetical group marker preserve physical
  rows with Markdown hard breaks; alphabetical markers start separate groups. Ordinary wrapped
  prose still joins, including a paragraph with a single inline equation. This is an example-band
  heuristic, not whole-entry reconstruction or a rule for every PDF line break.
- Runs of at least three aligned rows containing one to three words, or up to six words separated
  by a gap of at least twice the body point size, retain hard breaks without equations or markers
  when they have no terminal sentence punctuation. Only those compact runs
  are marked: adjacent wrapped prose still joins, with a paragraph boundary at each transition.
  Alignment tolerance is half the body point size, with a one-point minimum. This conservative
  short-list heuristic can still confuse unusually narrow prose and does not reconstruct columns.
- Section candidates recognize dictionary-style bands with at least three repeated example-column
  starts and a wide gutter after short, non-sentence headword fragments. Each left-column entry
  starts a paragraph; right-only continuation rows stay with the current entry. Crossing words
  or inconsistent gutters reject the interpretation. This is page-local and heuristic, not a
  complete dictionary schema or cross-page entry reconstruction.
- Leading ordinary bullet symbols are paired with their row text and normalized to Markdown list
  markers; wrapped continuation lines retain indentation. Warning triangles are not normalized as
  ordinary bullets. Normal periods, parentheses, exclamation marks and in-word hyphens are unescaped, while source
  markup and unintended leading numeric ordered-list syntax using either delimiter remain protected.
- Recognized bold contents headings enable row-based contents formatting instead of prose or
  per-entry headings. Two-column mode requires repeated numeric-edge/title-row evidence in both
  page halves. Rows retain reference placement and are emitted column by column; long dotted
  leaders are compacted. Wrapped contents entries and unusual column geometries still need review.
- Entry-row candidates preserve horizontal word/example associations by grouping nearby
  baselines. They deliberately do not claim whole-entry reconstruction: independent columns
  can be interleaved, and multi-line or cross-page continuations remain unresolved.
- Matching page-margin rows on at least two distinct pages are classified as repeated
  margin candidates, with digit runs normalized for matching. Classification is heuristic;
  top, bottom, left and right margins are considered. Inspect the excluded-margin view before
  accepting removal. Baseline text remains intact.
- Every nonblank source word must occur exactly once in each candidate or in the explicit
  excluded-margin set. Coverage checks require word identity as well as counts and reject
  missing/duplicated blocks. Nearest-neighbour extraction can emit whitespace-only words that
  Docstrum omits; those are not counted as missing source content.
- Source Markdown/HTML syntax is escaped in structured exports. Encoded capitalization is
  preserved rather than globally uppercasing German text; visual small-cap normalization,
  split-word/dehyphenation recovery, figure interpretation, table semantics and complete section
  hierarchy remain acceptance work. Section-band/gutter detection is heuristic, not general table recognition.

Read-only real-source checks verified that the reported manual footer appears in excluded
margin candidates and the reported foreword heading has a separate paragraph boundary.
These targeted checks do not establish correct reading order or semantic fidelity on every page.
No candidate is wired into production preparation, persisted generation identity or retrieval.
Algorithm versions are recorded in standalone Markdown and JSON, so changed candidates are not
silently presented as the previous algorithm. Markdown parsing/rendering can validate or display
syntax, but cannot recover missing PDF associations; no parser dependency is required for these
producer-side repairs.

Private originals and derived content persist until the operator removes the review directory;
do not commit, publish or upload them. Inspect real documents locally, not through automation
that captures DOM snapshots/screenshots into diagnostics or chat, including on tool errors.
Use synthetic content for automated UI diagnostics. Keep reviewed passage/order expectations in
private configuration rather than copying source text into tracked tests.

Local heuristic development is paused at `LayoutReviewV6`; managed extraction is evaluated instead
of adding more PDF-specific repairs. The local candidates remain comparison baselines, not accepted
semantic profiles.

The prior combined offline run passed 103 cases. The latest focused OpenAI run passed all
**28 cases**, including three added response-model-label cases. The current source-declared
offline inventory is 106: 52 local-layout/writer, 25 managed-client/state, one managed-export
and 28 OpenAI comparison cases; the full 106-case group was not rerun in this phase.
No cloud requests are made by this group. Use the `DocumentQuality` trait or fully qualified offline class names:
a suffix filter for `OpenAIDocumentQualityTests` also matches the separately gated live class.

#### Charged Document Intelligence Benchmark

The Debug-only [benchmark](Tests/Integration/LiveDocumentIntelligenceQualityTests.cs) requires
separate approval to submit the two complete PDFs to Azure and incur page-analysis charges.
`DocumentQuality:LiveEnabled` alone does not authorize it. The distinct
`DocumentIntelligenceBenchmark` category also requires `DocumentQuality:ManagedBenchmarkEnabled`.
Both gates precede credential/network operations; no application host or ingestion worker starts.

Supply an existing private local-review directory containing `source-1-sample-1` and
`source-2-sample-2` originals and their `LayoutReviewV6` JSON. Both originals must match the cached
SHA-256 provenance, and every selected local page must exist before the first submission.
The fixed plan analyzes physical PDF pages **2, 3, 6** from sample 1 and **6, 7, 12** from sample 2:
six pages total, at most two POSTs. Full PDF bytes are submitted; the `pages` option limits analysis.
Verify reported content anchors against physical pages, not printed page labels, before approving
a paid plan. The reported seasons passage is on physical page 8 and was not included or evaluated.

After explicit approval, configure private values from the repository root:

```powershell
$env:DocumentQuality__LocalConfigurationPath = (Resolve-Path .\appsettings.Local.json).Path
$env:DocumentQuality__LiveEnabled = 'true'
$env:DocumentQuality__ManagedBenchmarkEnabled = 'true'
$env:DocumentQuality__DocumentIntelligenceEndpoint = 'https://document-intelligence.example.com/'
$env:DocumentQuality__ManagedSourceDirectory = Join-Path $env:TEMP 'approved-local-review'
$env:DocumentQuality__ManagedOutputDirectory = Join-Path $env:TEMP 'managed-layout-benchmark'
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=DocumentIntelligenceBenchmark' --output Detailed
```

Replace the example endpoint/directories with approved private configuration. Use one fixed output
directory outside Git, separate from the original review; **do not create another directory to
work around uncertain submission state**. The client uses the application's Key Vault/certificate
bootstrap and shared Entra bearer handler with the Cognitive Services scope. It disables redirects,
HTTP-client logging and POST retries; no API key, public Blob URL, Azure CLI data-plane credential,
OpenAI call or paid add-on is used.

The pinned contract is `prebuilt-layout`, API `2024-11-30`, Markdown output and UTF-16 page spans.
Each PDF is capped at 20 MiB and each response at 16 MiB. The analysis phase has an eight-minute
overall cancellation deadline after credential initialization, a 90-second HTTP timeout, and at
most 90 polls per receipt with bounded 1-30 second delays honoring `Retry-After`.
Only same-origin pinned-model result URLs are polled.
Explicit cancellation checkpoints run before paid-operation state changes and network calls;
transport handlers must still honor the passed token while requests are in flight.

The [state helper](Tests/DocumentIntelligenceBenchmarkState.cs) writes a source/page/model manifest
before POST, then saves the operation receipt before polling. A manifest without a receipt/result
means the outcome is unknown: reconcile it explicitly, never automatically POST again. Saved
receipts resume GET polling; saved successful JSON is validated and reused without submission.
Changed source/options, malformed results, missing/duplicate pages and failed operations surface
explicit errors, not a local-extraction fallback. Preserve all state when diagnosing a failure.

Private output contains the exact original PDFs, request manifests, operation receipts, full raw
analysis JSON, complete managed Markdown, selected local Markdown, escaped three-column page
comparisons, a generic index and basic `metrics.json`. Existing exports must match byte-for-byte;
they are not overwritten. Metrics describe this invocation's wall time, page/text counts and
submission/reuse state, not an invoice, isolated service latency or semantic accuracy. Page-span
views can omit document-level markers; retain the complete Markdown and raw JSON. Provider output
may contain HTML tables; it is displayed as escaped text, never executed or fetched as content.
Use the same private-artifact handling and local-browser restrictions as other previews.

Observed approved live execution on 2026-10-26: **one passed, zero failed/skipped**, using the
application identity; two successful submissions returned exactly three requested pages each.
The samples produced 14,289 and 5,988 Markdown characters in approximately 13.2 and 7.8 seconds
respectively, including polling/export. Private structural checks found number examples 1-17 on
individual lines, all seven weekdays and 26 reported dictionary headwords in table rows, and no
unnecessary escaped ordinary punctuation on the three vocabulary pages. These are narrow
structural observations: manual contents/heading/warning fidelity, dictionary continuation
ownership, every other page and the unsubmitted seasons passage still require visual acceptance.
No derived output was chunked, embedded, indexed or uploaded, and ingestion remains disabled.
The approved live selection passed the baseline and preview cases and skipped semantic acceptance:
one private bundle was generated for each of the two configured sources. This does not replace
independent visual review or establish semantic chunking quality. The preview-only regeneration
with heading spacing also passed for both sources.

#### OpenAI PDF-Only And Hybrid Benchmark

The [client](Tests/OpenAIDocumentQualityClient.cs) and
[fixture](Tests/Integration/LiveOpenAIDocumentQualityTests.cs) add PDF-only and PDF-plus-managed-draft
candidates for the vocabulary sample's physical pages **6, 7, 12**. PdfPig copies one PDF page per
request; it does not generate either model candidate from local text extraction. Saved Document
Intelligence results are reused. The selected model is `gpt-5.5`; the approved run allows at most
six generation requests, each with at most 8,192 output tokens including low-effort reasoning.
Physical page 8 remains outside this plan.

The frozen `PdfReconciliationV1` prompt treats the original as authoritative, prefers ordinary
Markdown and permits simple two-column pairs as hyphenated list items when source text, headers
and entry/example ownership remain intact. It forbids summarizing, translating, language
simplification and invented content. Changes/uncertainties are separate schema fields, not source
text. Four-way private exports and case-sensitive word/number multiplicity differences aid review;
differences ignore order/punctuation and cannot prove fidelity or approve A1 vocabulary.

Configure these values privately in addition to the existing live configuration chain:

| `DocumentQuality` key | Contract |
| --- | --- |
| `OpenAIBenchmarkEnabled` | Separate explicit approval gate; default disabled, also requires `LiveEnabled` |
| `OpenAIEndpoint` | Approved credential-free HTTPS Azure OpenAI root endpoint |
| `OpenAIDeployment` / `OpenAIModelVersion` | Selected existing `gpt-5.5` deployment and verified revision |
| `OpenAIOutputDirectory` | Fixed private output directory outside Git, distinct from both input directories |
| `ManagedOutputDirectory` | Existing successful managed benchmark, including the original, request manifest and raw analysis |
| `ManagedSourceDirectory` | Existing hash-matching `LayoutReviewV6` JSON for the same original |

The current Responses v1 examples use the `https://ai.azure.com/.default` audience, distinct from
the Document Intelligence scope. The shared bearer handler uses the existing runtime credential,
not an API key or Azure CLI identity. Redirects, HTTP-client logging and automatic retries are
disabled. Responses use strict JSON schema, `store=false`, no tools/background execution, a
three-minute HTTP timeout and a fifteen-minute overall generation deadline after initialization.
Disabling response storage does not override Azure retention/abuse-monitoring policies.
Each response is capped at 16 MiB. Source/page hashes, endpoint fingerprint, model revision,
prompt/request hash, draft identity and output cap are pinned before POST. Raw successful responses
are saved before validation; incomplete/refused/malformed/wrong-page/model results fail explicitly.
Existing responses are reused; an intent without a successful response blocks resubmission.
Private rejected HTTP bodies/statuses are retained for diagnosis, never printed.
Azure may report the base model name rather than a dated snapshot identifier. The validator
accepts only the approved base name or exact snapshot and rejects other names/revisions.
Verify the selected deployment revision separately against live metadata before execution;
the base response name is not revision evidence. A parser correction can reuse a saved successful
response without another POST.

After explicit approval and private configuration, run only the non-generative access check:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=OpenAIDocumentAccess' --output Detailed
```

Only after access is resolved and saved failure state is explicitly reconciled/approved:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait 'Category=OpenAIDocumentBenchmark' --output Detailed
```

Observed on 2026-10-27: the initial endpoint rejected one POST and read-only model listing with
`SubscriptionNotRegistered` despite provider registration being `Registered`. After the separate
infrastructure handoff, renewed approval explicitly reconciled that rejected attempt. Its three
original artifacts were hash-verified and preserved in an attempt archive inside the same fixed
run directory, with a private reconciliation record; no state was deleted to bypass replay protection.

The alternative-subscription comparison completed six generation requests using `gpt-5.5`,
deployment revision `2026-04-24` verified through metadata. Initial parsing rejected the first
completed response's base model name; after the correction, the resumed test reused that response
and sent only the five remaining requests. Exactly one selected live comparison test passed with
zero failures/skips. It produced six exact Markdown/audit exports, saved request/raw-response
records, original/page PDFs, the frozen prompt and a four-way private comparison index; all
15 navigation targets were verified. No new managed analysis or production activation occurred.

| Candidate | Requests | Reported input tokens | Reported output tokens |
| --- | ---: | ---: | ---: |
| PDF-only | 3 | 2,462 | 1,948 |
| PDF plus managed draft | 3 | 7,151 | 1,981 |
| Total | 6 | 9,613 | 3,929 |

Every response stayed below 8,192 output tokens. Both candidates preserve individual rows for
numbers 1–17 and contain all seven weekday names on their respective pages; all six have no
HTML tags or unnecessarily escaped ordinary punctuation. Seven uncertainty items were reported
across the six candidates. These are structural checks and model reports, not semantic acceptance,
proof of A1 compliance, a measured invoice or evidence that hybrid is better than PDF-only.
Human comparison with the original remains required. Other applications' endpoint cutover is
not part of this test. See the
[ADR access handoff](../../docs/adr-0001-centralized-document-rag.md#openai-access-handoff-2026-10-27).

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 113 | ASP.NET Core host protocols, startup security, PostgreSQL persistence and Redis payload caching |
| `Agent Runtime` | 54 | Runtime orchestration, tools, metrics, evaluation, persistence, caching, locking and compaction |
| `Document Preparation` | 3 | Secret-free preparation template, editor constraint preservation and stateless runtime execution |
| `Tenant Isolation` | 38 | Cross-tenant state, JWT permissions, caller mappings, administrator assignment, MCP and provider credentials and identity isolation |
| `PostgreSQL` | 25 | Definitions, durable state, run history and shared Data Protection keys against the Compose tests-profile database |
| `Redis` | 8 | Revision-qualified state caching against the Compose tests-profile Redis service; also tagged `Integration` and `PostgreSQL` |
| `Local Agent Runtime` | 1 | Explicitly enabled smoke test against a complete running runtime |
| `DocumentQuality` | 106 Debug-only | Synthetic local/managed/OpenAI layout, paid-operation state and private-export checks; no cloud requests |
| `DocumentQualityLive` | 3 Debug-only | Opt-in live baseline, independent semantic acceptance and private previews; also tagged `Integration` |
| `DocumentQualityPreview` | 1 Debug-only | Separately opted-in persistent private review output; also tagged `Integration` and `DocumentQualityLive` |
| `DocumentIntelligenceBenchmark` | 1 Debug-only | Separately approved charged two-PDF/six-page managed analysis; also tagged `Integration`, not `DocumentQualityLive` |
| `OpenAIDocumentBenchmark` | 1 Debug-only | Separately approved six-request PDF-only/hybrid comparison; also tagged `Integration` |
| `OpenAIDocumentAccess` | 1 Debug-only | Separately gated, non-generative application-identity model-list access; also tagged `Integration` |

The category inventory above covers the runtime baseline. Debug adds one case to each of
`Integration` and `PostgreSQL` for the runtime logging soak; role/ingestion, PDF preparation and
Blob download/prepared-document categories and counts are listed separately above.

## Skipped Tests

| Condition / source-declared reason | Cases affected |
| --- | ---: |
| Live runtime tests require `CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS=true` outside CI | 1 |
| Once enabled, missing/invalid live base address, agent name or requested certificate-auth settings | 1 (same case) |
| Document-quality live tests require `DocumentQuality:LiveEnabled=true` | 3 Debug-only cases |
| Semantic document acceptance also requires `DocumentQuality:SemanticEnabled=true` | 1 (same semantic case) |
| Persistent private previews also require `DocumentQuality:PreviewEnabled=true` | 1 (same preview case) |
| Charged managed benchmark requires `DocumentQuality:ManagedBenchmarkEnabled=true` and `DocumentQuality:LiveEnabled=true` | 1 Debug-only case |
| OpenAI benchmark/access require `DocumentQuality:OpenAIBenchmarkEnabled=true` and `DocumentQuality:LiveEnabled=true` | 2 Debug-only cases |

These are conditional skips, not observed outcomes. PostgreSQL/Redis cases do not automatically
skip without their services: start the Compose tests profile or exclude both categories.

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
They never start ad hoc containers, touch the development database or flush Redis. See
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
├── DocumentIngestionTestData.cs
├── DocumentIntelligenceBenchmarkReview.cs
├── DocumentIntelligenceBenchmarkState.cs
├── DocumentIntelligenceQualityClient.cs
├── DocumentLayoutQuality.cs
├── DocumentLayoutReview.cs
├── DocumentPreparationTestData.cs
├── DocumentQualityReviewWriter.cs
├── OpenAIDocumentQualityClient.cs
├── OpenAIDocumentQualityReview.cs
├── ScriptedEvaluationAgentFactory.cs
├── Integration/
│   ├── AdministrationApiTests.cs
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
│   ├── DocumentPreparationPersistenceTests.cs
│   ├── LiveDocumentQualityTests.cs
│   ├── LiveDocumentIntelligenceQualityTests.cs
│   ├── LiveOpenAIDocumentQualityTests.cs
│   ├── HostIntegrationCollection.cs
│   ├── McpAgentizrWebApplicationFactory.cs
│   ├── PostgreSqlFixture.cs
│   ├── PostgresAgentDefinitionStoreIntegrationTests.cs
│   ├── AgentRunHistoryTests.cs
│   ├── PostgresAgentStateTests.cs
│   ├── RedisStateFixture.cs
│   ├── TenantSecurityStartupTests.cs
│   └── TestBase.cs
├── Local/
│   ├── LiveAgentRuntimeSmokeTests.cs
│   ├── LiveAgentRuntimeTestSettings.cs
│   ├── LocalAgentRuntimeFactAttribute.cs
│   └── LocalAgentRuntimeTestEnvironment.cs
└── Unit/
    ├── AdministrationAccessTests.cs
    ├── AdministrationSetupServiceTests.cs
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
    ├── DocumentIngestionAdmissionTests.cs
    ├── DocumentIngestionConfigTests.cs
    ├── DocumentIntelligenceBenchmarkReviewTests.cs
    ├── DocumentIntelligenceQualityClientTests.cs
    ├── DocumentLayoutQualityTests.cs
    ├── DocumentLayoutReviewTests.cs
    ├── DocumentQualityReviewWriterTests.cs
    ├── OpenAIDocumentQualityTests.cs
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

To run non-live coverage without Docker, append `--filter-not-trait 'Category=PostgreSQL' --filter-not-trait 'Category=Redis'` and
leave the local opt-in disabled. No tests were executed to produce this inventory.

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
