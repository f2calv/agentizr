using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentIngestionAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    blob_uri = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    sequencer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_documents", x => x.id);
                    table.UniqueConstraint("ak_knowledge_documents_tenant_collection_id", x => new { x.tenant_id, x.collection_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "document_ingestion_jobs",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sequencer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_ingestion_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_ingestion_jobs_document",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id },
                        principalSchema: "agent_runtime",
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "tenant_id", "collection_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_ingestion_jobs_tenant_collection_document",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                columns: new[] { "tenant_id", "collection_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_ingestion_jobs_tenant_collection_status",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                columns: new[] { "tenant_id", "collection_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_ingestion_jobs",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "knowledge_documents",
                schema: "agent_runtime");
        }
    }
}
