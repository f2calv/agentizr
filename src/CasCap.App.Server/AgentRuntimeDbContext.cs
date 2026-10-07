using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap;

/// <summary>PostgreSQL authority for versioned Agent Runtime definitions.</summary>
public sealed class AgentRuntimeDbContext(
    DbContextOptions<AgentRuntimeDbContext> options) : DbContext(options)
{
    private const string Schema = "agent_runtime";

    /// <summary>Gets immutable definition snapshots.</summary>
    public DbSet<AgentDefinitionSnapshotEntity> AgentDefinitionSnapshots => Set<AgentDefinitionSnapshotEntity>();

    /// <summary>Gets active definition pointers.</summary>
    public DbSet<ActiveAgentDefinitionEntity> ActiveAgentDefinitions => Set<ActiveAgentDefinitionEntity>();

    /// <summary>Gets append-only definition activation history.</summary>
    public DbSet<AgentDefinitionActivationEntity> AgentDefinitionActivations => Set<AgentDefinitionActivationEntity>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? null : Schema;

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
