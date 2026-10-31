using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_approvals",
                schema: "agent_runtime",
                columns: table => new
                {
                    generation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chunk_review_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    assessment_json = table.Column<string>(type: "jsonb", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_approvals", x => new { x.generation_id, x.revision });
                    table.ForeignKey(
                        name: "fk_document_approvals_generation",
                        columns: x => new { x.tenant_id, x.generation_id },
                        principalSchema: "agent_runtime",
                        principalTable: "document_markdown_generations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_approvals_tenant_generation",
                schema: "agent_runtime",
                table: "document_approvals",
                columns: new[] { "tenant_id", "generation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_approvals",
                schema: "agent_runtime");
        }
    }
}
