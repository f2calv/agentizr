using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentPreparedGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_preparation_generations",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequencer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    profile_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    profile_json = table.Column<string>(type: "jsonb", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    pages_without_text_json = table.Column<string>(type: "jsonb", nullable: false),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    prepared_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_preparation_generations", x => x.id);
                    table.UniqueConstraint("ak_document_preparation_generations_tenant_collection_id", x => new { x.tenant_id, x.collection_id, x.id });
                    table.ForeignKey(
                        name: "fk_document_preparation_generations_document",
                        columns: x => new { x.tenant_id, x.collection_id, x.document_id },
                        principalSchema: "agent_runtime",
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "tenant_id", "collection_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_text_chunks",
                schema: "agent_runtime",
                columns: table => new
                {
                    generation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_text_chunks", x => new { x.generation_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_document_text_chunks_generation",
                        columns: x => new { x.tenant_id, x.collection_id, x.generation_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_preparation_generations",
                        principalColumns: new[] { "tenant_id", "collection_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_preparation_generations_document",
                schema: "agent_runtime",
                table: "document_preparation_generations",
                columns: new[] { "tenant_id", "collection_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_text_chunks_tenant_collection_generation",
                schema: "agent_runtime",
                table: "document_text_chunks",
                columns: new[] { "tenant_id", "collection_id", "generation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_text_chunks",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "document_preparation_generations",
                schema: "agent_runtime");
        }
    }
}
