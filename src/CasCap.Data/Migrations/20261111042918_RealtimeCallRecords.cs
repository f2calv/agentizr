using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class RealtimeCallRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "realtime_calls",
                schema: "agent_runtime",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    provider_call_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usage_json = table.Column<string>(type: "jsonb", nullable: true),
                    summary_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realtime_calls", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_realtime_calls_status_lease_expires_at_utc",
                schema: "agent_runtime",
                table: "realtime_calls",
                columns: new[] { "status", "lease_expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "realtime_calls",
                schema: "agent_runtime");
        }
    }
}
