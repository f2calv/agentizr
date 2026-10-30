using CasCap.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CasCap;

/// <summary>PostgreSQL authority for Agent Runtime definitions, state, debugging history and shared Data Protection keys.</summary>
public sealed class AgentRuntimeDbContext(
    DbContextOptions<AgentRuntimeDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    private const string Schema = "agent_runtime";

    /// <summary>Gets latest tenant-qualified source document admission state.</summary>
    public DbSet<KnowledgeDocumentEntity> KnowledgeDocuments => Set<KnowledgeDocumentEntity>();

    /// <summary>Gets durable event receipts and queued document-processing intents.</summary>
    public DbSet<DocumentIngestionJobEntity> DocumentIngestionJobs => Set<DocumentIngestionJobEntity>();

    /// <summary>Gets durable synchronization requests and latest per-source run progress.</summary>
    public DbSet<DocumentSynchronizationEntity> DocumentSynchronizations => Set<DocumentSynchronizationEntity>();

    /// <summary>Gets immutable, non-searchable preparation generations.</summary>
    public DbSet<DocumentPreparationGenerationEntity> DocumentPreparationGenerations => Set<DocumentPreparationGenerationEntity>();

    /// <summary>Gets page-attributed text belonging to prepared generations.</summary>
    public DbSet<DocumentTextChunkEntity> DocumentTextChunks => Set<DocumentTextChunkEntity>();

    /// <summary>Gets tenant-qualified paid preparation intents, receipts, usage and immutable candidate projections.</summary>
    public DbSet<DocumentPreparationOperationEntity> DocumentPreparationOperations => Set<DocumentPreparationOperationEntity>();

    /// <summary>Gets immutable review-required Markdown generations, separate from page-local prepared chunks.</summary>
    public DbSet<DocumentMarkdownGenerationEntity> DocumentMarkdownGenerations => Set<DocumentMarkdownGenerationEntity>();

    /// <summary>Gets append-only chunk-selection decisions, never indexing approval.</summary>
    public DbSet<DocumentChunkReviewEntity> DocumentChunkReviews => Set<DocumentChunkReviewEntity>();

    /// <summary>Gets append-only document readiness assessments, never indexing activation.</summary>
    public DbSet<DocumentApprovalEntity> DocumentApprovals => Set<DocumentApprovalEntity>();

    /// <inheritdoc/>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Gets immutable definition snapshots.</summary>
    public DbSet<AgentDefinitionSnapshotEntity> AgentDefinitionSnapshots => Set<AgentDefinitionSnapshotEntity>();

    /// <summary>Gets active definition pointers.</summary>
    public DbSet<ActiveAgentDefinitionEntity> ActiveAgentDefinitions => Set<ActiveAgentDefinitionEntity>();

    /// <summary>Gets append-only definition activation history.</summary>
    public DbSet<AgentDefinitionActivationEntity> AgentDefinitionActivations => Set<AgentDefinitionActivationEntity>();

    /// <summary>Gets durable session, snapshot and override payloads.</summary>
    public DbSet<AgentStateEntity> AgentStates => Set<AgentStateEntity>();

    /// <summary>Gets durable execution history, independent of resumable state.</summary>
    public DbSet<AgentRunEntity> AgentRuns => Set<AgentRunEntity>();

    /// <summary>Gets ordered per-run messages and tool activity.</summary>
    public DbSet<AgentRunActivityEntity> AgentRunActivities => Set<AgentRunActivityEntity>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? null : Schema;

        modelBuilder.Entity<DocumentSynchronizationEntity>(entity =>
        {
            entity.ToTable("document_synchronizations", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_synchronizations");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.Requested).HasColumnName("requested");
            entity.Property(item => item.RequestedAtUtc).HasColumnName("requested_at_utc");
            entity.Property(item => item.RunId).HasColumnName("run_id");
            entity.Property(item => item.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(item => item.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(item => item.FinishedAtUtc).HasColumnName("finished_at_utc");
            entity.Property(item => item.LastSucceededAtUtc).HasColumnName("last_succeeded_at_utc");
            entity.Property(item => item.State).HasColumnName("state").HasMaxLength(30);
            entity.Property(item => item.AddedCount).HasColumnName("added_count");
            entity.Property(item => item.RepairedCount).HasColumnName("repaired_count");
            entity.Property(item => item.RemovedCount).HasColumnName("removed_count");
            entity.Property(item => item.UnchangedCount).HasColumnName("unchanged_count");
            entity.Property(item => item.ConflictCount).HasColumnName("conflict_count");
            entity.Property(item => item.FailureCode).HasColumnName("failure_code").HasMaxLength(40);
            entity.HasIndex(item => new { item.TenantId, item.CollectionId })
                .HasDatabaseName("ix_document_synchronizations_tenant_collection");
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                entity.Property(item => item.ExpiresAtUtc).HasConversion(
                    value => value!.Value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        });

        modelBuilder.Entity<KnowledgeDocumentEntity>(entity =>
        {
            entity.ToTable("knowledge_documents", schema);
            entity.HasKey(item => item.Id).HasName("pk_knowledge_documents");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.BlobUri).HasColumnName("blob_uri").HasMaxLength(4_000);
            entity.Property(item => item.Sequencer).HasColumnName("sequencer").HasMaxLength(128);
            entity.Property(item => item.ETag).HasColumnName("etag").HasMaxLength(200);
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(item => item.LastSeenSyncId).HasColumnName("last_seen_sync_id");
            entity.HasAlternateKey(item => new { item.TenantId, item.CollectionId, item.Id })
                .HasName("ak_knowledge_documents_tenant_collection_id");
        });
        modelBuilder.Entity<DocumentIngestionJobEntity>(entity =>
        {
            entity.ToTable("document_ingestion_jobs", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_ingestion_jobs");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.Sequencer).HasColumnName("sequencer").HasMaxLength(128);
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(item => item.AcceptedAtUtc).HasColumnName("accepted_at_utc");
            entity.Property(item => item.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            entity.Property(item => item.ClaimId).HasColumnName("claim_id");
            entity.Property(item => item.ClaimExpiresAtUtc).HasColumnName("claim_expires_at_utc");
            entity.Property(item => item.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
            entity.Property(item => item.FinishedAtUtc).HasColumnName("finished_at_utc");
            entity.Property(item => item.LastFailureCode).HasColumnName("last_failure_code").HasMaxLength(40);
            entity.Property(item => item.PreparationPlanJson).HasColumnName("preparation_plan_json").HasColumnType("jsonb");
            entity.HasAlternateKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .HasName("ak_document_ingestion_jobs_tenant_collection_document_id");
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                entity.Property(item => item.ClaimExpiresAtUtc).HasConversion(
                    value => value!.Value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
                entity.Property(item => item.NextAttemptAtUtc).HasConversion(
                    value => value!.Value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
                entity.Property(item => item.FinishedAtUtc).HasConversion(
                    value => value!.Value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
            }
            entity.HasIndex(item => new { item.Status, item.NextAttemptAtUtc })
                .HasDatabaseName("ix_document_ingestion_jobs_status_next_attempt");
            entity.HasIndex(item => new { item.Status, item.ClaimExpiresAtUtc })
                .HasDatabaseName("ix_document_ingestion_jobs_status_claim_expiry");
            entity.HasOne<KnowledgeDocumentEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_ingestion_jobs_document");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.Status })
                .HasDatabaseName("ix_document_ingestion_jobs_tenant_collection_status");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.DocumentId })
                .HasDatabaseName("ix_document_ingestion_jobs_tenant_collection_document");
        });

        modelBuilder.Entity<DocumentPreparationOperationEntity>(entity =>
        {
            entity.ToTable("document_preparation_operations", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_preparation_operations");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.JobId).HasColumnName("job_id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.SourceSha256).HasColumnName("source_sha256").HasMaxLength(64);
            entity.Property(item => item.PlanSha256).HasColumnName("plan_sha256").HasMaxLength(64);
            entity.Property(item => item.RequestSha256).HasColumnName("request_sha256").HasMaxLength(64);
            entity.Property(item => item.DerivedPdfSha256).HasColumnName("derived_pdf_sha256").HasMaxLength(64);
            entity.Property(item => item.Stage).HasColumnName("stage").HasMaxLength(40);
            entity.Property(item => item.PageNumber).HasColumnName("page_number");
            entity.Property(item => item.SubmissionClaimId).HasColumnName("submission_claim_id");
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(item => item.OperationUri).HasColumnName("operation_uri").HasMaxLength(4_000);
            entity.Property(item => item.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
            entity.Property(item => item.ResultSha256).HasColumnName("result_sha256").HasMaxLength(64);
            entity.Property(item => item.UsageJson).HasColumnName("usage_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(item => item.ReceivedAtUtc).HasColumnName("received_at_utc");
            entity.HasOne<DocumentIngestionJobEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.JobId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_preparation_operations_job");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.JobId })
                .HasDatabaseName("ix_document_preparation_operations_job");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.JobId, item.Stage, item.PageNumber })
                .IsUnique().HasDatabaseName("ux_document_preparation_operations_job_stage_page");
        });

        modelBuilder.Entity<DocumentMarkdownGenerationEntity>(entity =>
        {
            entity.ToTable("document_markdown_generations", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_markdown_generations");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.JobId).HasColumnName("job_id").HasMaxLength(64);
            entity.Property(item => item.Sequencer).HasColumnName("sequencer").HasMaxLength(128);
            entity.Property(item => item.ETag).HasColumnName("etag").HasMaxLength(200);
            entity.Property(item => item.ProjectionId).HasColumnName("projection_id").HasMaxLength(64);
            entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasAlternateKey(item => new { item.TenantId, item.Id })
                .HasName("ak_document_markdown_generations_tenant_id");
            entity.HasOne<DocumentIngestionJobEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.JobId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_markdown_generations_job");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.JobId })
                .HasDatabaseName("ix_document_markdown_generations_job");
        });

        modelBuilder.Entity<DocumentChunkReviewEntity>(entity =>
        {
            entity.ToTable("document_chunk_reviews", schema);
            entity.HasKey(item => new { item.GenerationId, item.Revision }).HasName("pk_document_chunk_reviews");
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.GenerationId).HasColumnName("generation_id").HasMaxLength(64);
            entity.Property(item => item.Revision).HasColumnName("revision");
            entity.Property(item => item.ActorId).HasColumnName("actor_id");
            entity.Property(item => item.ExclusionsJson).HasColumnName("exclusions_json").HasColumnType("jsonb");
            entity.Property(item => item.RecordedAtUtc).HasColumnName("recorded_at_utc");
            entity.HasOne<DocumentMarkdownGenerationEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.GenerationId })
                .HasPrincipalKey(item => new { item.TenantId, item.Id })
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_document_chunk_reviews_generation");
            entity.HasIndex(item => new { item.TenantId, item.GenerationId })
                .HasDatabaseName("ix_document_chunk_reviews_tenant_generation");
        });

        modelBuilder.Entity<DocumentApprovalEntity>(entity =>
        {
            entity.ToTable("document_approvals", schema);
            entity.HasKey(item => new { item.GenerationId, item.Revision }).HasName("pk_document_approvals");
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.GenerationId).HasColumnName("generation_id").HasMaxLength(64);
            entity.Property(item => item.Revision).HasColumnName("revision");
            entity.Property(item => item.ChunkReviewRevision).HasColumnName("chunk_review_revision");
            entity.Property(item => item.ActorId).HasColumnName("actor_id");
            entity.Property(item => item.AssessmentJson).HasColumnName("assessment_json").HasColumnType("jsonb");
            entity.Property(item => item.RecordedAtUtc).HasColumnName("recorded_at_utc");
            entity.HasOne<DocumentMarkdownGenerationEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.GenerationId })
                .HasPrincipalKey(item => new { item.TenantId, item.Id })
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_document_approvals_generation");
            entity.HasIndex(item => new { item.TenantId, item.GenerationId })
                .HasDatabaseName("ix_document_approvals_tenant_generation");
        });

        modelBuilder.Entity<DocumentPreparationGenerationEntity>(entity =>
        {
            entity.ToTable("document_preparation_generations", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_preparation_generations");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.Sequencer).HasColumnName("sequencer").HasMaxLength(128);
            entity.Property(item => item.ETag).HasColumnName("etag").HasMaxLength(200);
            entity.Property(item => item.SourceSha256).HasColumnName("source_sha256").HasMaxLength(64);
            entity.Property(item => item.ProfileId).HasColumnName("profile_id").HasMaxLength(64);
            entity.Property(item => item.ProfileJson).HasColumnName("profile_json").HasColumnType("jsonb");
            entity.Property(item => item.PageCount).HasColumnName("page_count");
            entity.Property(item => item.PagesWithoutTextJson).HasColumnName("pages_without_text_json").HasColumnType("jsonb");
            entity.Property(item => item.ChunkCount).HasColumnName("chunk_count");
            entity.Property(item => item.PreparedAtUtc).HasColumnName("prepared_at_utc");
            entity.HasAlternateKey(item => new { item.TenantId, item.CollectionId, item.Id })
                .HasName("ak_document_preparation_generations_tenant_collection_id");
            entity.HasOne<KnowledgeDocumentEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_preparation_generations_document");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.DocumentId })
                .HasDatabaseName("ix_document_preparation_generations_document");
        });
        modelBuilder.Entity<DocumentTextChunkEntity>(entity =>
        {
            entity.ToTable("document_text_chunks", schema);
            entity.HasKey(item => new { item.GenerationId, item.Ordinal }).HasName("pk_document_text_chunks");
            entity.Property(item => item.GenerationId).HasColumnName("generation_id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.Ordinal).HasColumnName("ordinal");
            entity.Property(item => item.PageNumber).HasColumnName("page_number");
            entity.Property(item => item.StartOffset).HasColumnName("start_offset");
            entity.Property(item => item.EndOffset).HasColumnName("end_offset");
            entity.Property(item => item.TokenCount).HasColumnName("token_count");
            entity.Property(item => item.Text).HasColumnName("text");
            entity.HasOne<DocumentPreparationGenerationEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.GenerationId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_text_chunks_generation");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.GenerationId })
                .HasDatabaseName("ix_document_text_chunks_tenant_collection_generation");
        });

        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("data_protection_keys", schema);
            entity.HasKey(key => key.Id).HasName("pk_data_protection_keys");
            entity.Property(key => key.Id).HasColumnName("id");
            entity.Property(key => key.FriendlyName).HasColumnName("friendly_name");
            entity.Property(key => key.Xml).HasColumnName("xml");
        });

        modelBuilder.Entity<AgentStateEntity>(entity =>
        {
            entity.ToTable("agent_states", schema);
            entity.HasKey(state => state.Key).HasName("pk_agent_states");
            entity.Property(state => state.Key).HasColumnName("key").HasMaxLength(200);
            entity.Property(state => state.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(state => state.AgentName).HasColumnName("agent_name").HasMaxLength(200);
            entity.Property(state => state.SessionId).HasColumnName("session_id");
            entity.Property(state => state.Revision).HasColumnName("revision");
            entity.Property(state => state.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(state => state.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(state => new { state.TenantId, state.AgentName })
                .HasDatabaseName("ix_agent_states_tenant_agent");
        });

        modelBuilder.Entity<AgentRunEntity>(entity =>
        {
            entity.ToTable("agent_runs", schema);
            entity.HasKey(run => run.Id).HasName("pk_agent_runs");
            entity.Property(run => run.Id).HasColumnName("id");
            entity.Property(run => run.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(run => run.AgentName).HasColumnName("agent_name").HasMaxLength(200);
            entity.Property(run => run.SessionId).HasColumnName("session_id");
            entity.Property(run => run.DefinitionVersion).HasColumnName("definition_version").HasMaxLength(100);
            entity.Property(run => run.ModelName).HasColumnName("model_name");
            entity.Property(run => run.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(run => run.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(run => run.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(run => run.ErrorType).HasColumnName("error_type");
            entity.Property(run => run.SummaryJson).HasColumnName("summary_json").HasColumnType("jsonb");
            entity.HasIndex(run => new { run.TenantId, run.AgentName, run.StartedAtUtc })
                .HasDatabaseName("ix_agent_runs_tenant_agent_started");
        });

        modelBuilder.Entity<AgentRunActivityEntity>(entity =>
        {
            entity.ToTable("agent_run_activities", schema);
            entity.HasKey(activity => activity.Id).HasName("pk_agent_run_activities");
            entity.Property(activity => activity.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(activity => activity.RunId).HasColumnName("run_id");
            entity.Property(activity => activity.RecordedAtUtc).HasColumnName("recorded_at_utc");
            entity.Property(activity => activity.ActivityJson).HasColumnName("activity_json").HasColumnType("jsonb");
            entity.HasOne<AgentRunEntity>().WithMany().HasForeignKey(activity => activity.RunId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_agent_run_activities_run");
            entity.HasIndex(activity => new { activity.RunId, activity.Id })
                .HasDatabaseName("ix_agent_run_activities_run_id");
        });

        modelBuilder.Entity<AgentDefinitionSnapshotEntity>(entity =>
        {
            entity.ToTable("agent_definition_snapshots", schema);
            entity.HasKey(snapshot => snapshot.Id).HasName("pk_agent_definition_snapshots");
            entity.Property(snapshot => snapshot.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(snapshot => snapshot.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(snapshot => snapshot.AgentName).HasColumnName("agent_name").HasMaxLength(200);
            entity.Property(snapshot => snapshot.DefinitionVersion).HasColumnName("definition_version").HasMaxLength(100);
            entity.Property(snapshot => snapshot.SchemaVersion).HasColumnName("schema_version");
            entity.Property(snapshot => snapshot.DefinitionJson).HasColumnName("definition_json").HasColumnType("jsonb");
            entity.Property(snapshot => snapshot.PublishedAtUtc).HasColumnName("published_at_utc").HasDefaultValueSql("NOW()");
            entity.Property(snapshot => snapshot.PublishedBy).HasColumnName("published_by").HasMaxLength(200);
            entity.Property(snapshot => snapshot.ChangeReason).HasColumnName("change_reason").HasMaxLength(2_000);
            entity.HasIndex(snapshot => new { snapshot.TenantId, snapshot.AgentName, snapshot.DefinitionVersion })
                .IsUnique()
                .HasDatabaseName("ux_agent_definition_snapshots_tenant_agent_version");
            entity.HasAlternateKey(snapshot => new { snapshot.TenantId, snapshot.AgentName, snapshot.Id })
                .HasName("ak_agent_definition_snapshots_tenant_agent_id");
            entity.HasIndex(snapshot => new { snapshot.TenantId, snapshot.AgentName, snapshot.PublishedAtUtc })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_agent_definition_snapshots_tenant_agent_published");
        });

        modelBuilder.Entity<ActiveAgentDefinitionEntity>(entity =>
        {
            entity.ToTable("active_agent_definitions", schema);
            entity.HasKey(active => new { active.TenantId, active.AgentName })
                .HasName("pk_active_agent_definitions");
            entity.Property(active => active.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(active => active.AgentName).HasColumnName("agent_name").HasMaxLength(200);
            entity.Property(active => active.SnapshotId).HasColumnName("snapshot_id");
            entity.Property(active => active.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("NOW()");
            entity.HasOne(active => active.Snapshot)
                .WithMany()
                .HasForeignKey(active => new { active.TenantId, active.AgentName, active.SnapshotId })
                .HasPrincipalKey(snapshot => new { snapshot.TenantId, snapshot.AgentName, snapshot.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_active_agent_definitions_snapshot");
            entity.HasIndex(active => new { active.TenantId, active.AgentName, active.SnapshotId })
                .HasDatabaseName("ix_active_agent_definitions_tenant_agent_snapshot");
        });

        modelBuilder.Entity<AgentDefinitionActivationEntity>(entity =>
        {
            entity.ToTable("agent_definition_activations", schema);
            entity.HasKey(activation => activation.Id).HasName("pk_agent_definition_activations");
            entity.Property(activation => activation.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(activation => activation.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(activation => activation.AgentName).HasColumnName("agent_name").HasMaxLength(200);
            entity.Property(activation => activation.SnapshotId).HasColumnName("snapshot_id");
            entity.Property(activation => activation.ActivatedAtUtc).HasColumnName("activated_at_utc").HasDefaultValueSql("NOW()");
            entity.Property(activation => activation.ActivatedBy).HasColumnName("activated_by").HasMaxLength(200);
            entity.Property(activation => activation.ChangeReason).HasColumnName("change_reason").HasMaxLength(2_000);
            entity.Property(activation => activation.IsDeactivation).HasColumnName("is_deactivation").HasDefaultValue(false);
            entity.HasOne(activation => activation.Snapshot)
                .WithMany()
                .HasForeignKey(activation => new { activation.TenantId, activation.AgentName, activation.SnapshotId })
                .HasPrincipalKey(snapshot => new { snapshot.TenantId, snapshot.AgentName, snapshot.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_agent_definition_activations_snapshot");
            entity.HasIndex(activation => new { activation.TenantId, activation.AgentName, activation.Id })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_agent_definition_activations_tenant_agent_id");
            entity.HasIndex(activation => new { activation.TenantId, activation.AgentName, activation.SnapshotId })
                .HasDatabaseName("ix_agent_definition_activations_tenant_agent_snapshot");
        });
    }
}
