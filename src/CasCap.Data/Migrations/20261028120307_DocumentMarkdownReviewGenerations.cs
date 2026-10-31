using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentMarkdownReviewGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_markdown_generations",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequencer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    projection_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_markdown_generations", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_markdown_generations_job",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id, x.job_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_ingestion_jobs",
                        principalColumns: new[] { "tenant_id", "collection_id", "document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_markdown_generations_job",
                schema: "agent_runtime",
                table: "document_markdown_generations",
                columns: new[] { "tenant_id", "collection_id", "document_id", "job_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_markdown_generations",
                schema: "agent_runtime");
        }
    }
}
