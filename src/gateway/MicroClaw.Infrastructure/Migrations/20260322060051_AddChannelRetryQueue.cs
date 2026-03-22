using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelRetryQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_retry_queue",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    channel_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    message_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_text = table.Column<string>(type: "TEXT", nullable: false),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    next_retry_at = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    last_error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_retry_queue", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_retry_queue_message_id",
                table: "channel_retry_queue",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_retry_queue_status",
                table: "channel_retry_queue",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_retry_queue");
        }
    }
}
