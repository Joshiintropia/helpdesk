using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SegmentSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sla_policies_area_id_priority",
                table: "sla_policies");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_paused_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_responded_at",
                table: "ticket_segments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_response_due_at",
                table: "ticket_segments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolution_due_at",
                table: "ticket_segments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_paused_minutes",
                table: "ticket_segments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sla_policy_id",
                table: "ticket_segments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_area_id_priority",
                table: "sla_policies",
                columns: new[] { "area_id", "priority" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sla_policies_area_id_priority",
                table: "sla_policies");

            migrationBuilder.DropColumn(
                name: "sla_paused_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "first_responded_at",
                table: "ticket_segments");

            migrationBuilder.DropColumn(
                name: "first_response_due_at",
                table: "ticket_segments");

            migrationBuilder.DropColumn(
                name: "resolution_due_at",
                table: "ticket_segments");

            migrationBuilder.DropColumn(
                name: "sla_paused_minutes",
                table: "ticket_segments");

            migrationBuilder.DropColumn(
                name: "sla_policy_id",
                table: "ticket_segments");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_area_id_priority",
                table: "sla_policies",
                columns: new[] { "area_id", "priority" },
                unique: true);
        }
    }
}
