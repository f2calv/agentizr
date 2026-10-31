# CasCap.Backend.Tests

.NET 10 xUnit v3/Microsoft.Testing.Platform coverage for
[CasCap.Backend](../CasCap.Backend/README.md): application services, authorization, state,
document processing and offline migration/model discovery. This project does not reference the
server executable.

## Tests

Counts below are source-declared methods and expanded InlineData cases, not observed run results.
Debug-only document and logging cases are absent from Release where guarded by `#if DEBUG`.

| Class | Method count | Test-case count |
| --- | ---: | ---: |
| `AdministrationAccessTests` | 5 | 14 |
| `AdministrationSetupServiceTests` | 3 | 5 |
| `AgentDefinitionEvaluatorTests` | 4 | 6 |
| `AgentExecutionCoordinatorTests` | 7 | 12 |
| `AgentRuntimeAuthorizationTests` | 4 | 6 |
| `AgentRuntimeBuiltInToolsTests` | 3 | 5 |
| `AgentRuntimeMetricsTests` | 5 | 5 |
| `AgentSessionLockTests` | 3 | 5 |
| `AuthenticatedTenantContextTests` | 4 | 4 |
| `CachedAgentDefinitionStoreTests` | 3 | 3 |
| `CommonAgentExecutorTests` | 7 | 7 |
| `ConfigurationMcpCredentialStoreTests` | 2 | 2 |
| `ConfigurationProviderCredentialStoreTests` | 2 | 5 |
| `DocumentBlobDownloadTests` | 5 | 21 |
| `DocumentChunkingTests` | 5 | 15 |
| `DocumentEmbeddingProviderTests` | 3 | 16 |
| `DocumentIndexingConfigTests` | 4 | 32 |
| `DocumentIndexingPlanTests` | 6 | 34 |
| `DocumentIngestionAdmissionTests` | 9 | 20 |
| `DocumentIngestionConfigTests` | 7 | 23 |
| `DocumentMarkdownChunkingTests` | 17 | 30 |
| `DocumentMarkdownReviewTests` | 10 | 32 |
| `DocumentMarkdownStructureTests` | 10 | 33 |
| `DocumentOllamaEmbeddingProviderTests` | 2 | 16 |
| `DocumentPreparationAgentTemplateTests` | 3 | 3 |
| `DocumentPreparationCandidateTests` | 2 | 12 |
| `DocumentPreparationConfigTests` | 3 | 6 |
| `DocumentPreparationModelVerifierTests` | 3 | 12 |
| `DocumentPreparationOfflineReviewTests` | 8 | 14 |
| `DocumentPreparationPlanTests` | 1 | 17 |
| `FeatureConfigTests` | 3 | 11 |
| `PdfDocumentPreparationTests` | 15 | 21 |
| `PostgresAgentDefinitionStoreTests` | 5 | 5 |
| `RuntimeLoggingAuthorizationTests` | 3 | 9 |
| `SessionCompactionConfigTests` | 3 | 6 |
| `TenantStateIsolationTests` | 2 | 2 |
| `PersistenceModelTests` | 1 | 1 |

`PersistenceModelTests` initializes only design-time model/migration metadata: it does not connect
to PostgreSQL or apply migrations. Provider-backed and `WebApplicationFactory<Program>` checks
remain in [Server.Tests](../CasCap.App.Server.Tests/README.md).

## Trait Categories

`Agent Runtime`, `Authorization`, `BlobDownload`, `Document Preparation`, `HybridPreparation`,
`Indexing`, `Ingestion`, `Integration`, `MarkdownChunking`, `OfflineDocumentReview`,
`PdfPreparation`, `Roles` and `Tenant Isolation`.

## Skipped Tests

One Debug case requires explicit retained-document review approval:
`DocumentPreparationOfflineReviewTests.RetainedCandidatesToChunks` is gated by
`CASCAP_RUN_DOCUMENT_OFFLINE_REVIEW=true` and requires local review input plus a fresh private
output directory. Ordinary runs must not read operator documents. See the
[retained-review guidance](../CasCap.App.Server.Tests/README.md#retained-real-document-review)
for the additional settings and input/output precautions.

## Layout

```text
Tests/
├── *TestData.cs / ScriptedEvaluationAgentFactory.cs / DocumentPreparationReviewRenderer.cs
├── DocumentPreparationHttpHandler.cs
├── Unit/          # Application services and opt-in retained review
└── Integration/
    └── PersistenceModelTests.cs  # Offline EF metadata, no database access
```

Server.Tests links the shared root helper sources; it does not reference this test executable or
duplicate the fixtures.

## Run

Run from the repository root only when test execution is intended:

```powershell
dotnet test --project src\CasCap.Backend.Tests\CasCap.Backend.Tests.csproj --configuration Debug
```

Debug uses adjacent shared-library checkouts; Release uses published dependencies. Backend tests
do not load server appsettings or deployment credentials.
