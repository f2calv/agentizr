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
