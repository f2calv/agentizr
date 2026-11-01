using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Migrations
{
    /// <inheritdoc />
    public partial class DocumentIngestionProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claim_expires_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "claim_id",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finished_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_failure_code",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_ingestion_jobs_status_claim_expiry",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                columns: new[] { "status", "claim_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_document_ingestion_jobs_status_next_attempt",
                schema: "agent_runtime",
                table: "document_ingestion_jobs",
                columns: new[] { "status", "next_attempt_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_ingestion_jobs_status_claim_expiry",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropIndex(
                name: "ix_document_ingestion_jobs_status_next_attempt",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "claim_expires_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "claim_id",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "finished_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "last_failure_code",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                schema: "agent_runtime",
                table: "document_ingestion_jobs");
        }
    }
}
