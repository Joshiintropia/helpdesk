using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsSurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rated_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "satisfaction_comment",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "satisfaction_rating",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<long>(type: "bigint", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_attachments_asp_net_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_attachments_ticket_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "ticket_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ticket_attachments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_message_id",
                table: "ticket_attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_ticket_id",
                table: "ticket_attachments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_unlinked",
                table: "ticket_attachments",
                column: "created_at",
                filter: "ticket_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_uploaded_by_id",
                table: "ticket_attachments",
                column: "uploaded_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "rated_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "satisfaction_comment",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "satisfaction_rating",
                table: "tickets");
        }
    }
}
