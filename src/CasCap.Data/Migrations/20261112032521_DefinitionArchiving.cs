using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DefinitionArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at_utc",
                schema: "agent_runtime",
                table: "agent_definition_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "archived_by",
                schema: "agent_runtime",
                table: "agent_definition_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archived_at_utc",
                schema: "agent_runtime",
                table: "agent_definition_snapshots");

            migrationBuilder.DropColumn(
                name: "archived_by",
                schema: "agent_runtime",
                table: "agent_definition_snapshots");
        }
    }
}
