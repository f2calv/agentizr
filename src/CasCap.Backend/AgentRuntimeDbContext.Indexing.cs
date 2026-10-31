using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap;

public sealed partial class AgentRuntimeDbContext
{
    private static void ConfigureDocumentIndexing(ModelBuilder modelBuilder, string? schema)
    {
        modelBuilder.Entity<DocumentIndexingJobEntity>(entity =>
        {
            entity.ToTable("document_indexing_jobs", schema);
            entity.HasKey(item => item.Id).HasName("pk_document_indexing_jobs");
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.PreparationJobId).HasColumnName("preparation_job_id").HasMaxLength(64);
            entity.Property(item => item.GenerationId).HasColumnName("generation_id").HasMaxLength(64);
            entity.Property(item => item.ManifestSha256).HasColumnName("manifest_sha256").HasMaxLength(64);
            entity.Property(item => item.ManifestJson).HasColumnName("manifest_json").HasColumnType("jsonb");
            entity.Property(item => item.PolicySha256).HasColumnName("policy_sha256").HasMaxLength(64);
            entity.Property(item => item.PolicyRevision).HasColumnName("policy_revision");
            entity.Property(item => item.ActorId).HasColumnName("actor_id");
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(40);
            entity.Property(item => item.ExpectedActiveJobId).HasColumnName("expected_active_job_id").HasMaxLength(64);
            entity.Property(item => item.MaximumRequests).HasColumnName("maximum_requests");
            entity.Property(item => item.MaximumInputTokens).HasColumnName("maximum_input_tokens");
            entity.Property(item => item.SubmittedRequests).HasColumnName("submitted_requests");
            entity.Property(item => item.ReceivedInputTokens).HasColumnName("received_input_tokens");
            entity.Property(item => item.CompletedBatches).HasColumnName("completed_batches");
            entity.Property(item => item.VectorCount).HasColumnName("vector_count");
            entity.Property(item => item.ClaimId).HasColumnName("claim_id");
            entity.Property(item => item.ClaimExpiresAtUtc).HasColumnName("claim_expires_at_utc");
            entity.Property(item => item.AcceptedAtUtc).HasColumnName("accepted_at_utc");
            entity.Property(item => item.FinishedAtUtc).HasColumnName("finished_at_utc");
            entity.HasAlternateKey(item => new { item.TenantId, item.Id }).HasName("ak_document_indexing_jobs_tenant_id");
            entity.HasAlternateKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .HasName("ak_document_indexing_jobs_source_id");
            entity.HasOne<DocumentIngestionJobEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.PreparationJobId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_document_indexing_jobs_preparation");
            entity.HasIndex(item => new { item.TenantId, item.CollectionId, item.PolicyRevision })
                .HasDatabaseName("ix_document_indexing_jobs_policy");
            entity.HasIndex(item => item.Status).HasDatabaseName("ix_document_indexing_jobs_status");
        });
        modelBuilder.Entity<DocumentEmbeddingBatchEntity>(entity =>
        {
            entity.ToTable("document_embedding_batches", schema);
            entity.HasKey(item => new { item.JobId, item.Ordinal }).HasName("pk_document_embedding_batches");
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.JobId).HasColumnName("job_id").HasMaxLength(64);
            entity.Property(item => item.Ordinal).HasColumnName("ordinal");
            entity.Property(item => item.ClaimId).HasColumnName("claim_id");
            entity.Property(item => item.InputSha256).HasColumnName("input_sha256").HasMaxLength(64);
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(40);
            entity.Property(item => item.InputTokens).HasColumnName("input_tokens");
            entity.Property(item => item.ResultSha256).HasColumnName("result_sha256").HasMaxLength(64);
            entity.Property(item => item.SubmittedAtUtc).HasColumnName("submitted_at_utc");
            entity.Property(item => item.ReceivedAtUtc).HasColumnName("received_at_utc");
            entity.HasOne<DocumentIndexingJobEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.JobId })
                .HasPrincipalKey(item => new { item.TenantId, item.Id })
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_document_embedding_batches_job");
        });
        modelBuilder.Entity<ActiveDocumentIndexEntity>(entity =>
        {
            entity.ToTable("active_document_indexes", schema);
            entity.HasKey(item => new { item.TenantId, item.CollectionId, item.DocumentId })
                .HasName("pk_active_document_indexes");
            entity.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(200);
            entity.Property(item => item.CollectionId).HasColumnName("collection_id").HasMaxLength(200);
            entity.Property(item => item.DocumentId).HasColumnName("document_id").HasMaxLength(64);
            entity.Property(item => item.JobId).HasColumnName("job_id").HasMaxLength(64);
            entity.Property(item => item.ActivatedAtUtc).HasColumnName("activated_at_utc");
            entity.HasOne<DocumentIndexingJobEntity>().WithMany()
                .HasForeignKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.JobId })
                .HasPrincipalKey(item => new { item.TenantId, item.CollectionId, item.DocumentId, item.Id })
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_active_document_indexes_job");
        });
    }
}
