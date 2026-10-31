using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentVectorIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $prerequisite$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector') THEN
                        RAISE EXCEPTION 'Document indexing requires the operator-installed vector extension.';
                    END IF;
                END
                $prerequisite$;
                """);
            migrationBuilder.CreateTable(
                name: "document_indexing_jobs",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preparation_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    generation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: true),
                    policy_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_revision = table.Column<int>(type: "integer", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expected_active_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    maximum_requests = table.Column<int>(type: "integer", nullable: false),
                    maximum_input_tokens = table.Column<int>(type: "integer", nullable: false),
                    submitted_requests = table.Column<int>(type: "integer", nullable: false),
                    received_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    completed_batches = table.Column<int>(type: "integer", nullable: false),
                    vector_count = table.Column<int>(type: "integer", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_indexing_jobs", x => x.id);
                    table.UniqueConstraint("ak_document_indexing_jobs_source_id", x => new { x.tenant_id, x.collection_id, x.document_id, x.id });
                    table.UniqueConstraint("ak_document_indexing_jobs_tenant_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_document_indexing_jobs_preparation",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id, x.preparation_job_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_ingestion_jobs",
                        principalColumns: new[] { "tenant_id", "collection_id", "document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // SQL-owned: EF models durable jobs and receipts without requiring a vector plugin in every context.
            migrationBuilder.Sql("""
                CREATE TABLE agent_runtime.document_index_vectors (
                    tenant_id character varying(200) NOT NULL,
                    job_id character varying(64) NOT NULL,
                    ordinal integer NOT NULL CHECK (ordinal >= 0),
                    input_sha256 character varying(64) NOT NULL,
                    embedding vector NOT NULL CHECK (vector_dims(embedding) BETWEEN 1 AND 16000),
                    CONSTRAINT pk_document_index_vectors PRIMARY KEY (job_id, ordinal),
                    CONSTRAINT fk_document_index_vectors_job FOREIGN KEY (tenant_id, job_id)
                        REFERENCES agent_runtime.document_indexing_jobs (tenant_id, id) ON DELETE CASCADE
                );
                """);

            migrationBuilder.CreateTable(
                name: "active_document_indexes",
                schema: "agent_runtime",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_document_indexes", x => new { x.tenant_id, x.collection_id, x.document_id });
                    table.ForeignKey(
                        name: "fk_active_document_indexes_job",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id, x.job_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_indexing_jobs",
                        principalColumns: new[] { "tenant_id", "collection_id", "document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_embedding_batches",
                schema: "agent_runtime",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    result_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_embedding_batches", x => new { x.job_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_document_embedding_batches_job",
                        columns: x => new { x.tenant_id, x.job_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_indexing_jobs",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_active_document_indexes_tenant_id_collection_id_document_id~",
                schema: "agent_runtime",
                table: "active_document_indexes",
                columns: new[] { "tenant_id", "collection_id", "document_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_embedding_batches_tenant_id_job_id",
                schema: "agent_runtime",
                table: "document_embedding_batches",
                columns: new[] { "tenant_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_indexing_jobs_policy",
                schema: "agent_runtime",
                table: "document_indexing_jobs",
                columns: new[] { "tenant_id", "collection_id", "policy_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_document_indexing_jobs_status",
                schema: "agent_runtime",
                table: "document_indexing_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_document_indexing_jobs_tenant_id_collection_id_document_id_~",
                schema: "agent_runtime",
                table: "document_indexing_jobs",
                columns: new[] { "tenant_id", "collection_id", "document_id", "preparation_job_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE agent_runtime.document_index_vectors;");
            migrationBuilder.DropTable(
                name: "active_document_indexes",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "document_embedding_batches",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "document_indexing_jobs",
                schema: "agent_runtime");
        }
    }
}
