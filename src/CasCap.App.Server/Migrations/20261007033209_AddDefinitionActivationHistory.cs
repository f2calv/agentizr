using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class AddDefinitionActivationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_definition_activations",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    activated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_definition_activations", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_definition_activations_snapshot",
                        columns: x => new { x.tenant_id, x.agent_name, x.snapshot_id },
                        principalSchema: "agent_runtime",
                        principalTable: "agent_definition_snapshots",
                        principalColumns: new[] { "tenant_id", "agent_name", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_definition_activations_tenant_agent_id",
                schema: "agent_runtime",
                table: "agent_definition_activations",
                columns: new[] { "tenant_id", "agent_name", "id" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_agent_definition_activations_tenant_agent_snapshot",
                schema: "agent_runtime",
                table: "agent_definition_activations",
                columns: new[] { "tenant_id", "agent_name", "snapshot_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_definition_activations",
                schema: "agent_runtime");
        }
    }
}
