using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DurableRuntimeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_runs",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    definition_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_name = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_type = table.Column<string>(type: "text", nullable: true),
                    summary_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_states",
                schema: "agent_runtime",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_states", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_activities",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activity_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_run_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_run_activities_run",
                        column: x => x.run_id,
                        principalSchema: "agent_runtime",
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_run_activities_run_id",
                schema: "agent_runtime",
                table: "agent_run_activities",
                columns: new[] { "run_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_tenant_agent_started",
                schema: "agent_runtime",
                table: "agent_runs",
                columns: new[] { "tenant_id", "agent_name", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_states_tenant_agent",
                schema: "agent_runtime",
                table: "agent_states",
                columns: new[] { "tenant_id", "agent_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_run_activities",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "agent_states",
                schema: "agent_runtime");

            migrationBuilder.DropTable(
                name: "agent_runs",
                schema: "agent_runtime");
        }
    }
}
