using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBoundChannelIdsAddSubAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bound_channel_ids_json",
                table: "agents");

            migrationBuilder.AddColumn<string>(
                name: "agent_id",
                table: "sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_session_id",
                table: "sessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "parent_session_id",
                table: "sessions");

            migrationBuilder.AddColumn<string>(
                name: "bound_channel_ids_json",
                table: "agents",
                type: "TEXT",
                nullable: true);
        }
    }
}
