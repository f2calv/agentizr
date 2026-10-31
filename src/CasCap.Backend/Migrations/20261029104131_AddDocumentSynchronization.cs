using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_seen_sync_id",
                schema: "agent_runtime",
                table: "knowledge_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_synchronizations",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    collection_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested = table.Column<bool>(type: "boolean", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_succeeded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    added_count = table.Column<int>(type: "integer", nullable: false),
                    repaired_count = table.Column<int>(type: "integer", nullable: false),
                    removed_count = table.Column<int>(type: "integer", nullable: false),
                    unchanged_count = table.Column<int>(type: "integer", nullable: false),
                    conflict_count = table.Column<int>(type: "integer", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_synchronizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_synchronizations_tenant_collection",
                schema: "agent_runtime",
                table: "document_synchronizations",
                columns: new[] { "tenant_id", "collection_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_synchronizations",
                schema: "agent_runtime");

            migrationBuilder.DropColumn(
                name: "last_seen_sync_id",
                schema: "agent_runtime",
                table: "knowledge_documents");
        }
    }
}
