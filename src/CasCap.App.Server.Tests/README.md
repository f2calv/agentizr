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
| `RuntimeLoggingTests` (Debug only) | 1 | 1 | Real PostgreSQL publications change the Serilog pipeline and forwarded logs without losing a process-cache sentinel; category overrides remain intact |
| **Runtime baseline subtotal** | **147** | **225** | Excludes Debug-only logging; role/ingestion additions are listed separately below |

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
| `DocumentIngestionCompositionTests` | 1 | 4 | Real local/hybrid-router DI with runtime infrastructure already registered, disabled/enabled gates, explicitly removed processor rejection and test substitution; no transport starts |
| **Role/ingestion subtotal** | **36** | **70** | All but the role-parser class are Debug-only |

Run the focused credential-free selection from the repository root:

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class '*FeatureConfigTests' '*DocumentIngestion*'
```

The database/DI fixtures are under `Integration/` and use synthetic, test-owned setup rather than
any live-service configuration; no Azure, PostgreSQL or Redis connection is made.
The composition fixture exercises `runtimeEnabled=true`. Full ingestion-only startup calls the
existing eager Redis registration and therefore needs a separately approved isolated Redis check;
the worker's tenant-local runtime adapter is independently covered without HTTP tenancy.
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
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-class CasCap.Tests.Integration.DocumentPreparationPersistenceTests
```

All 36 cases passed within the 178-case combined run. The full Debug solution builds cleanly;
offline EF checks report no pending model changes and the additive preparation migration SQL was
reviewed. No deployment migration was applied; the provider acceptance below applies the full chain
only to disposable local databases. SQLite alone does not prove PostgreSQL locks/races, live identity,
JSONB storage, embeddings, searchable activation or cleanup/retention behavior.

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
| [`DocumentHybridPreparationTests`](Tests/Integration/DocumentHybridPreparationTests.cs) | 16 | 30 | Real admission/PDF processing, pinned plans, selected original pages and source limits, single-submission receipts, exact claim/tenant fences, foreign keys, cancellation-time accounting, expired-intent blocking, managed GET resume, recorded-response reuse and review-only candidates |
| [`DocumentPreparationProviderTests`](Tests/Integration/DocumentPreparationProviderTests.cs) | 1 | 4 | Actual worker adapter, framework executor and SDK with fake HTTP: worker-local tenant credentials, selected physical-page authorization/schema, output ceiling, no ambient HTTP tenancy or definition re-resolution |
| [`DocumentPreparationModelVerifierTests`](Tests/Unit/DocumentPreparationModelVerifierTests.cs) | 3 | 12 | Bounded read-only ARM requests, exact account/endpoint/deployment/model/revision binding, redacted failure, oversized metadata and pre-cancellation |
| [`DocumentPreparationPlanTests`](Tests/Unit/DocumentPreparationPlanTests.cs) | 1 | 17 | Explicit budgets, distinct physical-page selections and supported stateless, secret-free, exact-schema Azure Responses authorization |
| [`DocumentPreparationCandidateTests`](Tests/Unit/DocumentPreparationCandidateTests.cs) | 2 | 12 | Markdown whitespace/entity projection without receipt mutation, encoded preview content, separate/collapsed changes, conditional page uncertainties and no inline usage |
| **Hybrid subtotal** | **23** | **75** | Local implementation proof, not PostgreSQL concurrency, Azure access, document fidelity or invoice reconciliation |

```powershell
dotnet test --project src\CasCap.App.Server.Tests --configuration Debug --filter-trait Category=HybridPreparation
```

All 75 cases passed with zero failures/skips. The focused run including the built-in template and
administration definition UI regressions passed **87 cases**, zero failed/skipped. These validate
the `PdfReconciliationV2` prompt and template discovery without changing installed definitions.
The actual SDK response-observer regressions also
exercise receipt observation before malformed/incomplete output is rejected. Missing token usage
stays unknown, and no test enables ingestion, activates a definition or replays a paid request.
The shared Azure layout transport has its own fake-handler suite. Markdown passthrough,
pricing estimates, preview UI and operator reconciliation are not
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
Private three-panel original/draft/agent previews and usage exports were saved; document fidelity
still requires independent review. This local-identity result does not establish deployed inference
access, queue-to-answer behavior or searchable activation.
The review renderer keeps transcription in its own element, hides empty review annotations, labels
nonempty **Page Uncertainties** and collapses **Review changes (not document content)**. Native usage
stays in the separate usage exports/ledger, never in the Markdown. The saved preview was refreshed
offline with this presentation and whitespace cleanup; neither raw receipts nor usage exports were
changed, and no new provider call was made.

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
and the then-current 53 offline hybrid regressions. The hybrid selection now has 75 cases; the
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
acceptance and protected preview UI remain separate gates in
[ADR 0001](../../docs/adr-0001-centralized-document-rag.md#built-in-document-preparation-agent).

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 122 | ASP.NET Core host protocols, synthetic definition-page rendering, startup security, PostgreSQL persistence and Redis payload caching |
| `Generation` | 7 | Credential-free definition-version suggestions with a fixed clock |
| `Agent Runtime` | 54 | Runtime orchestration, tools, metrics, evaluation, persistence, caching, locking and compaction |
| `Document Preparation` | 3 | Secret-free preparation template, editor constraint preservation and stateless runtime execution |
| `HybridPreparation` | 75 Debug-only | Bounded worker/runtime, selected original pages, Markdown/review separation, paid receipts/recovery, profile authorization and fake read-only model metadata; 34 cases also tagged `Integration` |
| `LiveDocumentPreparation` | 1 Debug-only | Separately approved real-source/provider acceptance with a retained local ledger; disabled by default |
| `PreparationProviders` | 22 Debug-only | Real isolated PostgreSQL ledger/recovery and two-host Redis startup/handover; all also `Integration` and `PostgreSQL`, one also `Redis` |
| `Tenant Isolation` | 38 | Cross-tenant state, JWT permissions, caller mappings, administrator assignment, MCP and provider credentials and identity isolation |
| `PostgreSQL` | 25 | Definitions, durable state, run history and shared Data Protection keys against local Compose PostgreSQL |
| `Redis` | 8 | Revision-qualified state caching against local Compose Redis; also tagged `Integration` and `PostgreSQL` |
| `Local Agent Runtime` | 1 | Explicitly enabled smoke test against a complete running runtime |

The category inventory above covers the runtime baseline. Debug adds one case to each of
`Integration` and `PostgreSQL` for the runtime logging soak; role/ingestion, PDF preparation and
Blob download/prepared-document, hybrid and provider-acceptance categories and counts are listed
separately above.

## Skipped Tests

| Condition / source-declared reason | Cases affected |
| --- | ---: |
| Live runtime tests require `CASCAP_RUN_LOCAL_AGENTRUNTIME_TESTS=true` outside CI | 1 |
| Once enabled, missing/invalid live base address, agent name or requested certificate-auth settings | 1 (same case) |
| Live preparation requires separate approval and `CASCAP_RUN_DOCUMENT_PREPARATION_ACCEPTANCE=true` | 1 Debug-only |

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
├── DocumentHybridPreparationTestData.cs
├── DocumentIngestionTestData.cs
├── DocumentPreparationReviewRenderer.cs
├── DocumentPreparationTestData.cs
├── ScriptedEvaluationAgentFactory.cs
├── Integration/
│   ├── AdministrationApiTests.cs
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
│   ├── DocumentPreparationLiveAcceptanceTests.cs
│   ├── DocumentPreparationPersistenceTests.cs
│   ├── DocumentPreparationProviderTests.cs
│   ├── HostIntegrationCollection.cs
│   ├── McpAgentizrWebApplicationFactory.cs
│   ├── PostgreSqlFixture.cs
│   ├── PostgresAgentDefinitionStoreIntegrationTests.cs
│   ├── PostgresDocumentPreparationIntegrationTests.cs
│   ├── PostgresDocumentPreparationPersistenceTests.cs
│   ├── AgentRunHistoryTests.cs
│   ├── PostgresAgentStateTests.cs
│   ├── RedisStateFixture.cs
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
    ├── DocumentIngestionAdmissionTests.cs
    ├── DocumentIngestionConfigTests.cs
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
