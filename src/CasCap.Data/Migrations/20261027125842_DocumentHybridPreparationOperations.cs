using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentHybridPreparationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preparation_plan_json",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_document_ingestion_jobs_tenant_collection_document_id",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                columns: new[] { "tenant_id", "collection_id", "document_id", "id" });

            migrationBuilder.CreateTable(
                name: "document_preparation_operations",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plan_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    derived_pdf_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    submission_claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    operation_uri = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    result_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    usage_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_preparation_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_preparation_operations_job",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id, x.job_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_ingestion_jobs",
                        principalColumns: new[] { "tenant_id", "collection_id", "document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_preparation_operations_job",
                schema: "agent_runtime",
                table: "document_preparation_operations",
                columns: new[] { "tenant_id", "collection_id", "document_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ux_document_preparation_operations_job_stage_page",
                schema: "agent_runtime",
                table: "document_preparation_operations",
                columns: new[] { "tenant_id", "collection_id", "job_id", "stage", "page_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_preparation_operations",
                schema: "agent_runtime");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_document_ingestion_jobs_tenant_collection_document_id",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "preparation_plan_json",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");
        }
    }
}
