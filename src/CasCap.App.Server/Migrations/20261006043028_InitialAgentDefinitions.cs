using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class InitialAgentDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agent_runtime");

            migrationBuilder.CreateTable(
                name: "agent_definition_snapshots",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    definition_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    published_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_definition_snapshots", x => x.id);
                    table.UniqueConstraint("ak_agent_definition_snapshots_tenant_agent_id", x => new { x.tenant_id, x.agent_name, x.id });
                });

            migrationBuilder.CreateTable(
                name: "active_agent_definitions",
                schema: "agent_runtime",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_agent_definitions", x => new { x.tenant_id, x.agent_name });
                    table.ForeignKey(
                        name: "fk_active_agent_definitions_snapshot",
                        columns: x => new { x.tenant_id, x.agent_name, x.snapshot_id },
                        principalSchema: "agent_runtime",
                        principalTable: "agent_definition_snapshots",
                        principalColumns: new[] { "tenant_id", "agent_name", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_agent_definitions_tenant_agent_snapshot",
                schema: "agent_runtime",
                table: "active_agent_definitions",
                columns: new[] { "tenant_id", "agent_name", "snapshot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_definition_snapshots_tenant_agent_published",
                schema: "agent_runtime",
                table: "agent_definition_snapshots",
                columns: new[] { "tenant_id", "agent_name", "published_at_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_agent_definition_snapshots_tenant_agent_version",
                schema: "agent_runtime",
                table: "agent_definition_snapshots",
                columns: new[] { "tenant_id", "agent_name", "definition_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_agent_definitions",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "agent_definition_snapshots",
                schema: "agent_runtime");
        }
    }
}
